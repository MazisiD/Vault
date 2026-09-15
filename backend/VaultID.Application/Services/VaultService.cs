using System.Security.Cryptography;
using System.Text;
using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Domain.Categories;
using VaultID.Domain.Events;
using VaultID.Domain.Models;

namespace VaultID.Application.Services;

/// <summary>
/// User-facing vault use cases: create a vault and populate/update fields
/// (blueprint 5.1, 5.3). Updating a field emits a FieldUpdated event and then
/// triggers "update once, propagate everywhere" notifications to organisations
/// that currently reference the affected category.
/// </summary>
public sealed class VaultService(
    VaultStreamRepository repository,
    WebhookNotificationService notifications)
{
    private readonly VaultStreamRepository _repository = repository;
    private readonly WebhookNotificationService _notifications = notifications;

    /// <summary>
    /// Creates a new vault, seeded with the 3 system categories and their
    /// field definitions (blueprint 5.1; dynamic-categories spec seeding).
    /// </summary>
    public async Task<VaultSummary> CreateVaultAsync(CreateVaultRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);

        var state = await _repository.LoadStateAsync(request.UserId, ct);
        if (state.Exists)
        {
            throw new ConflictException($"A vault already exists for user '{request.UserId}'.");
        }

        var categories = new List<Category>();
        var fields = new List<FieldDefinition>();

        foreach (var seedCategory in CategoryCatalog.SystemCategories)
        {
            var categoryId = Guid.NewGuid();
            categories.Add(new Category
            {
                Id = categoryId,
                Name = seedCategory.Name,
                IsSystem = true
            });

            var sortOrder = 0;
            foreach (var seedField in seedCategory.Fields)
            {
                fields.Add(new FieldDefinition
                {
                    Id = Guid.NewGuid(),
                    CategoryId = categoryId,
                    ParentFieldDefinitionId = null,
                    Name = seedField.Name,
                    FieldType = seedField.FieldType,
                    AutocompleteToken = null,
                    Choices = null,
                    SortOrder = sortOrder++
                });
            }
        }

        var created = new VaultCreated
        {
            VaultId = request.UserId,
            DisplayName = request.DisplayName,
            Categories = categories,
            Fields = fields
        };

        await _repository.AppendAsync(request.UserId, state.Version, [created], ct);
        return await GetSummaryAsync(request.UserId, ct);
    }

    /// <summary>Returns a lightweight summary of the vault.</summary>
    public async Task<VaultSummary> GetSummaryAsync(string userId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        if (!state.Exists)
        {
            throw new NotFoundException($"No vault found for user '{userId}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var activeShares = state.Grants.Values.Count(g => g.IsCurrentlyActive(now));

        var categories = state.Categories.Values
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CategorySummary(c.Id, c.Name, c.IsSystem))
            .ToList();

        return new VaultSummary(
            state.UserId,
            state.DisplayName,
            state.Exists,
            categories,
            activeShares);
    }

    /// <summary>Returns the current field values for one category (owner view).</summary>
    public async Task<CategoryView> GetCategoryAsync(string userId, Guid categoryId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        if (!state.Exists)
        {
            throw new NotFoundException($"No vault found for user '{userId}'.");
        }

        if (!state.Categories.ContainsKey(categoryId))
        {
            throw new NotFoundException($"Category '{categoryId}' not found.");
        }

        return new CategoryView(categoryId, state.GetCategoryValues(categoryId));
    }

    /// <summary>
    /// Updates a single field (blueprint 5.3). A thin wrapper over
    /// <see cref="UpdateCategoryFieldsAsync"/> so both paths validate, record
    /// and propagate identically.
    /// </summary>
    public async Task UpdateFieldAsync(string userId, UpdateFieldRequest request, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        if (!state.Exists)
        {
            throw new NotFoundException($"No vault found for user '{userId}'.");
        }

        if (!state.FieldDefinitions.TryGetValue(request.FieldDefinitionId, out var fieldDefinition))
        {
            throw new ValidationException($"Field '{request.FieldDefinitionId}' does not exist.");
        }

        await ApplyFieldValuesAsync(
            userId,
            state,
            fieldDefinition.CategoryId,
            [new FieldValueUpdate(request.FieldDefinitionId, request.Value)],
            ct);
    }

    /// <summary>
    /// Saves every edit the user made to one category in a single change
    /// (blueprint 5.3). The whole set is validated first, so one bad value
    /// rejects the save rather than leaving the category half-written, and the
    /// accepted values are then appended as one atomic batch.
    /// <para>
    /// Auditing stays field-level: a separate FieldUpdated event is recorded
    /// for each field whose value actually changed, and fields the user
    /// re-submitted unchanged produce no event at all.
    /// </para>
    /// </summary>
    public async Task<CategoryView> UpdateCategoryFieldsAsync(
        string userId, UpdateCategoryFieldsRequest request, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        if (!state.Exists)
        {
            throw new NotFoundException($"No vault found for user '{userId}'.");
        }

        if (!state.Categories.ContainsKey(request.CategoryId))
        {
            throw new NotFoundException($"Category '{request.CategoryId}' not found.");
        }

        await ApplyFieldValuesAsync(userId, state, request.CategoryId, request.Values, ct);
        return await GetCategoryAsync(userId, request.CategoryId, ct);
    }

    /// <summary>
    /// Validates every submitted value against its field definition, then
    /// appends one FieldUpdated event per genuinely changed field in a single
    /// write and propagates the change to organisations sharing the category.
    /// Nothing is written unless all of the values are valid.
    /// </summary>
    private async Task ApplyFieldValuesAsync(
        string userId,
        VaultState state,
        Guid categoryId,
        IReadOnlyList<FieldValueUpdate> values,
        CancellationToken ct)
    {
        state.Values.TryGetValue(categoryId, out var currentValues);

        var events = new List<DomainEvent>();
        var changedFieldIds = new List<Guid>();

        foreach (var update in values)
        {
            if (!state.FieldDefinitions.TryGetValue(update.FieldDefinitionId, out var fieldDefinition))
            {
                throw new ValidationException($"Field '{update.FieldDefinitionId}' does not exist.");
            }

            if (fieldDefinition.CategoryId != categoryId)
            {
                throw new ValidationException($"Field '{fieldDefinition.Name}' does not belong to this category.");
            }

            if (!FieldValueValidator.TryValidate(fieldDefinition, update.Value, out var error))
            {
                throw new ValidationException(error!);
            }

            string? oldValue = null;
            currentValues?.TryGetValue(update.FieldDefinitionId, out oldValue);

            // A field the user opened but left alone is not a change, and must
            // not litter the audit trail with a no-op edit.
            if (string.Equals(oldValue, update.Value, StringComparison.Ordinal))
            {
                continue;
            }

            events.Add(new FieldUpdated
            {
                VaultId = userId,
                FieldDefinitionId = update.FieldDefinitionId,
                NewValue = update.Value,
                OldValueHash = oldValue is null ? null : Hash(oldValue)
            });
            changedFieldIds.Add(update.FieldDefinitionId);
        }

        if (events.Count == 0)
        {
            return;
        }

        // One append for the whole category: the batch either lands in full or
        // not at all, and it consumes a single expected-version slot.
        await _repository.AppendAsync(userId, state.Version, events, ct);

        // "Update once, propagate everywhere": notify organisations that hold an
        // active share of this category. This appends a PropagationSent event
        // per changed field.
        await _notifications.PropagateFieldChangeAsync(userId, categoryId, changedFieldIds, ct);
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}

using System.Security.Cryptography;
using System.Text;
using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Domain.Categories;
using VaultID.Domain.Events;

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
    /// Updates a single field (blueprint 5.3). Validates the value against the
    /// field definition's type, emits FieldUpdated, then propagates the change
    /// to orgs that currently share that field's category.
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

        if (!FieldValueValidator.TryValidate(fieldDefinition, request.Value, out var error))
        {
            throw new ValidationException(error!);
        }

        string? oldValue = null;
        if (state.Values.TryGetValue(fieldDefinition.CategoryId, out var bucket))
        {
            bucket.TryGetValue(request.FieldDefinitionId, out oldValue);
        }

        var updated = new FieldUpdated
        {
            VaultId = userId,
            FieldDefinitionId = request.FieldDefinitionId,
            NewValue = request.Value,
            OldValueHash = oldValue is null ? null : Hash(oldValue)
        };

        await _repository.AppendAsync(userId, state.Version, [updated], ct);

        // "Update once, propagate everywhere": notify organisations that hold an
        // active share of this category. This appends a PropagationSent event.
        await _notifications.PropagateFieldChangeAsync(userId, fieldDefinition.CategoryId, request.FieldDefinitionId, ct);
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}

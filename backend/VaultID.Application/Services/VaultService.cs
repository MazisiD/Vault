using System.Security.Cryptography;
using System.Text;
using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Domain;
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
                SeedField(fields, categoryId, parentId: null, seedField, sortOrder++);
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

    /// <summary>
    /// Turns one catalog entry into a field definition with a fresh id, then
    /// does the same for its sub-fields - the parts of a group, or the template
    /// each item of a collection is made from.
    /// </summary>
    private static void SeedField(
        List<FieldDefinition> fields,
        Guid categoryId,
        Guid? parentId,
        CategoryCatalog.SeedField seed,
        int sortOrder)
    {
        var id = Guid.NewGuid();
        fields.Add(new FieldDefinition
        {
            Id = id,
            CategoryId = categoryId,
            ParentFieldDefinitionId = parentId,
            Name = seed.Name,
            FieldType = seed.FieldType,
            AutocompleteToken = null,
            Choices = seed.Choices,
            IsSecret = seed.Secret,
            ItemNoun = seed.ItemNoun,
            IsItemTitle = seed.IsItemTitle,
            SortOrder = sortOrder
        });

        var childSortOrder = 0;
        foreach (var child in seed.Children ?? [])
        {
            SeedField(fields, categoryId, id, child, childSortOrder++);
        }
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

        return BuildCategoryView(state, categoryId);
    }

    /// <summary>
    /// Assembles one category's owner-facing values: the ordinary fields, plus
    /// the items of every Collection the category defines, each in the order the
    /// user arranged them.
    /// </summary>
    private static CategoryView BuildCategoryView(VaultState state, Guid categoryId)
    {
        var collections = state.FieldDefinitions.Values
            .Where(f => f.CategoryId == categoryId && f.FieldType == FieldType.Collection)
            .ToDictionary(
                f => f.Id,
                f => (IReadOnlyList<CollectionItemView>)state.ItemsOf(f.Id)
                    .Select(itemId => new CollectionItemView(itemId, state.GetItemValues(itemId)))
                    .ToList());

        return new CategoryView(categoryId, state.GetCategoryValues(categoryId), collections);
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
            collections: null,
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

        await ApplyFieldValuesAsync(userId, state, request.CategoryId, request.Values, request.Collections, ct);
        return await GetCategoryAsync(userId, request.CategoryId, ct);
    }

    /// <summary>
    /// Validates every submitted value against its field definition, then
    /// appends the whole category's changes in a single write and propagates
    /// them to organisations sharing the category. Nothing is written unless
    /// all of the values are valid.
    /// <para>
    /// Collections are submitted as the complete picture the user wants, so the
    /// difference against what the vault currently holds is what gets recorded:
    /// items that disappeared are removed, unfamiliar item ids are added, and
    /// only genuinely changed item values produce an event.
    /// </para>
    /// </summary>
    private async Task ApplyFieldValuesAsync(
        string userId,
        VaultState state,
        Guid categoryId,
        IReadOnlyList<FieldValueUpdate> values,
        IReadOnlyList<CollectionUpdate>? collections,
        CancellationToken ct)
    {
        state.Values.TryGetValue(categoryId, out var currentValues);

        var events = new List<DomainEvent>();
        var changedFieldIds = new List<Guid>();

        foreach (var update in values)
        {
            var fieldDefinition = RequireCategoryField(state, categoryId, update.FieldDefinitionId);

            if (fieldDefinition.ParentFieldDefinitionId is { } ownerId
                && state.FieldDefinitions.TryGetValue(ownerId, out var owner)
                && owner.FieldType == FieldType.Collection)
            {
                throw new ValidationException(
                    $"'{fieldDefinition.Name}' belongs to the collection '{owner.Name}', so its value must be sent as part of an item.");
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

        foreach (var collection in collections ?? [])
        {
            CollectEventsForCollection(userId, state, categoryId, collection, events, changedFieldIds);
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

    /// <summary>
    /// Works out what changed in one Collection field and records it: the items
    /// the user dropped, the ones they added, and the values they edited.
    /// </summary>
    private static void CollectEventsForCollection(
        string userId,
        VaultState state,
        Guid categoryId,
        CollectionUpdate update,
        List<DomainEvent> events,
        List<Guid> changedFieldIds)
    {
        var collection = RequireCategoryField(state, categoryId, update.FieldDefinitionId);
        if (collection.FieldType != FieldType.Collection)
        {
            throw new ValidationException($"'{collection.Name}' is not a collection, so it has no items.");
        }

        var template = state.ChildrenOf(collection.Id).ToDictionary(c => c.Id);
        var existingItemIds = state.ItemsOf(collection.Id);
        var submittedItemIds = update.Items.Select(i => i.ItemId).ToHashSet();

        foreach (var removedItemId in existingItemIds.Where(id => !submittedItemIds.Contains(id)))
        {
            events.Add(new CollectionItemRemoved
            {
                VaultId = userId,
                FieldDefinitionId = collection.Id,
                ItemId = removedItemId
            });
            changedFieldIds.Add(collection.Id);
        }

        var sortOrder = 0;
        foreach (var item in update.Items)
        {
            if (!existingItemIds.Contains(item.ItemId))
            {
                events.Add(new CollectionItemAdded
                {
                    VaultId = userId,
                    FieldDefinitionId = collection.Id,
                    ItemId = item.ItemId,
                    SortOrder = sortOrder
                });
                changedFieldIds.Add(collection.Id);
            }

            CollectEventsForItem(userId, state, collection, template, item, events, changedFieldIds);
            sortOrder++;
        }
    }

    /// <summary>Records the values the user changed inside one collection item.</summary>
    private static void CollectEventsForItem(
        string userId,
        VaultState state,
        FieldDefinition collection,
        IReadOnlyDictionary<Guid, FieldDefinition> template,
        CollectionItemUpdate item,
        List<DomainEvent> events,
        List<Guid> changedFieldIds)
    {
        var currentItemValues = state.GetItemValues(item.ItemId);

        foreach (var value in item.Values)
        {
            if (!template.TryGetValue(value.FieldDefinitionId, out var child))
            {
                throw new ValidationException(
                    $"Field '{value.FieldDefinitionId}' is not part of the '{collection.Name}' collection.");
            }

            if (!FieldValueValidator.TryValidate(child, value.Value, out var error))
            {
                throw new ValidationException(error!);
            }

            currentItemValues.TryGetValue(value.FieldDefinitionId, out var oldValue);
            if (string.Equals(oldValue, value.Value, StringComparison.Ordinal))
            {
                continue;
            }

            events.Add(new CollectionItemFieldUpdated
            {
                VaultId = userId,
                ItemId = item.ItemId,
                FieldDefinitionId = value.FieldDefinitionId,
                NewValue = value.Value,
                OldValueHash = oldValue is null ? null : Hash(oldValue)
            });
            changedFieldIds.Add(value.FieldDefinitionId);
        }
    }

    private static FieldDefinition RequireCategoryField(VaultState state, Guid categoryId, Guid fieldId)
    {
        if (!state.FieldDefinitions.TryGetValue(fieldId, out var field))
        {
            throw new ValidationException($"Field '{fieldId}' does not exist.");
        }

        if (field.CategoryId != categoryId)
        {
            throw new ValidationException($"Field '{field.Name}' does not belong to this category.");
        }

        return field;
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}

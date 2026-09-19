using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Domain;
using VaultID.Domain.Categories;
using VaultID.Domain.Events;
using VaultID.Domain.Models;

namespace VaultID.Application.Services;

/// <summary>
/// Category and field-definition schema mutations (dynamic-categories spec,
/// "Mutation rules"). These are vault-owner-only operations with no sharing
/// implications of their own: they change what fields exist on the vault
/// owner's own vault, not who can see them. All mutations are event-sourced
/// and enforce the guardrails in <see cref="CategorySchemaGuard"/>:
/// <list type="bullet">
/// <item>System categories can never be renamed or deleted.</item>
/// <item>A category can't be deleted while it has fields.</item>
/// <item>A scalar field can't be deleted while it has a value.</item>
/// <item>
/// A Group field's delete cascades to its children only if every child is
/// itself deletable (no value); otherwise it is blocked, naming the child
/// that has data.
/// </item>
/// <item>
/// Adding a sub-field under a plain field promotes that field to a Group
/// (blocked while it holds a value); deleting a Group's last sub-field
/// demotes it back.
/// </item>
/// </list>
/// </summary>
public sealed class CategorySchemaService(VaultStreamRepository repository)
{
    private readonly VaultStreamRepository _repository = repository;

    public async Task<CategorySchemaView> CreateCategoryAsync(string userId, CreateCategoryRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        var state = await RequireVaultAsync(userId, ct);

        var created = new CategoryCreated
        {
            VaultId = userId,
            CategoryId = Guid.NewGuid(),
            Name = request.Name,
            IsSystem = false
        };

        await _repository.AppendAsync(userId, state.Version, [created], ct);
        return new CategorySchemaView(created.CategoryId, created.Name, false, []);
    }

    public async Task RenameCategoryAsync(string userId, Guid categoryId, string newName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var state = await RequireVaultAsync(userId, ct);
        var category = RequireCategory(state, categoryId);

        if (!CategorySchemaGuard.CanRename(category))
        {
            throw new ValidationException($"'{category.Name}' is a system category and cannot be renamed.");
        }

        var renamed = new CategoryRenamed { VaultId = userId, CategoryId = categoryId, NewName = newName };
        await _repository.AppendAsync(userId, state.Version, [renamed], ct);
    }

    public async Task DeleteCategoryAsync(string userId, Guid categoryId, CancellationToken ct = default)
    {
        var state = await RequireVaultAsync(userId, ct);
        var category = RequireCategory(state, categoryId);
        var hasFields = state.FieldsOf(categoryId).Count > 0;

        if (!CategorySchemaGuard.CanDelete(category, hasFields))
        {
            throw new ConflictException(category.IsSystem
                ? $"'{category.Name}' is a system category and cannot be deleted."
                : $"'{category.Name}' still has fields and cannot be deleted.");
        }

        var deleted = new CategoryDeleted { VaultId = userId, CategoryId = categoryId };
        await _repository.AppendAsync(userId, state.Version, [deleted], ct);
    }

    public async Task<FieldDefinitionView> CreateFieldAsync(
        string userId, Guid categoryId, CreateFieldRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        var state = await RequireVaultAsync(userId, ct);
        RequireCategory(state, categoryId);

        // The vault UI adds a field by name alone; anything richer (a type, an
        // autocomplete token, a choice list) is optional.
        var fieldType = request.FieldType ?? FieldType.Text;

        // A sub-field added under a plain scalar field promotes that field to a
        // Group, so the UI never has to ask the user to pick "Group" up front.
        var parentToPromote = ResolveParentToPromote(state, categoryId, request);
        ValidateNewField(fieldType, request);

        var sortOrder = state.FieldDefinitions.Values
            .Count(f => f.CategoryId == categoryId && f.ParentFieldDefinitionId == request.ParentFieldDefinitionId);

        var fieldId = Guid.NewGuid();
        var itemNoun = fieldType == FieldType.Collection ? request.ItemNoun : null;
        var events = new List<DomainEvent>();

        if (parentToPromote is not null)
        {
            events.Add(new FieldDefinitionUpdated
            {
                VaultId = userId,
                FieldDefinitionId = parentToPromote.Id,
                NewFieldType = FieldType.Group
            });
        }

        events.Add(new FieldDefinitionCreated
        {
            VaultId = userId,
            FieldDefinitionId = fieldId,
            CategoryId = categoryId,
            ParentFieldDefinitionId = request.ParentFieldDefinitionId,
            Name = request.Name,
            FieldType = fieldType,
            AutocompleteToken = request.AutocompleteToken,
            Choices = request.Choices,
            IsSecret = request.IsSecret,
            ItemNoun = itemNoun,
            SortOrder = sortOrder
        });

        await _repository.AppendAsync(userId, state.Version, events, ct);

        return new FieldDefinitionView(
            fieldId, categoryId, request.ParentFieldDefinitionId, request.Name,
            fieldType, request.AutocompleteToken, request.Choices, sortOrder, [],
            request.IsSecret, itemNoun);
    }

    /// <summary>
    /// Checks that the requested parent can actually take a sub-field and
    /// returns it when adding this child turns it into a group, or null when
    /// there is nothing to promote.
    /// </summary>
    private static FieldDefinition? ResolveParentToPromote(
        VaultState state, Guid categoryId, CreateFieldRequest request)
    {
        if (request.ParentFieldDefinitionId is not { } parentId)
        {
            return null;
        }

        if (!state.FieldDefinitions.TryGetValue(parentId, out var parent) || parent.CategoryId != categoryId)
        {
            throw new ValidationException($"Parent field '{parentId}' does not exist in this category.");
        }

        if (parent.ParentFieldDefinitionId is not null)
        {
            throw new ValidationException(
                $"'{parent.Name}' is already a sub-field; nesting is limited to one level.");
        }

        if (parent.IsContainer)
        {
            return null;
        }

        var parentHasValue = state.GetCategoryValues(categoryId)
            .TryGetValue(parent.Id, out var parentValue) && parentValue is not null;

        if (!CategorySchemaGuard.CanPromoteToGroup(parent, parentHasValue))
        {
            throw new ConflictException(
                $"'{parent.Name}' already holds a value, so it cannot become a group of sub-fields. Clear it first.");
        }

        return parent;
    }

    private static void ValidateNewField(FieldType fieldType, CreateFieldRequest request)
    {
        if (request.AutocompleteToken is { } token && !AutocompleteTokens.IsValid(token))
        {
            throw new ValidationException($"'{token}' is not a recognised autocomplete token.");
        }

        if (fieldType == FieldType.Choice && (request.Choices is null || request.Choices.Count == 0))
        {
            throw new ValidationException("Choice fields require at least one choice.");
        }

        if (fieldType is FieldType.Group or FieldType.Collection && request.ParentFieldDefinitionId is not null)
        {
            throw new ValidationException(
                "Containers can only be nested one level deep; a group or collection cannot contain another.");
        }

        if (fieldType == FieldType.Collection && string.IsNullOrWhiteSpace(request.ItemNoun))
        {
            throw new ValidationException("A collection needs a name for one of its items, e.g. 'bank account'.");
        }
    }

    public async Task RenameFieldAsync(
        string userId, Guid categoryId, Guid fieldId, string newName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var state = await RequireVaultAsync(userId, ct);
        RequireCategory(state, categoryId);
        RequireField(state, categoryId, fieldId);

        var renamed = new FieldDefinitionUpdated { VaultId = userId, FieldDefinitionId = fieldId, NewName = newName };
        await _repository.AppendAsync(userId, state.Version, [renamed], ct);
    }

    public async Task DeleteFieldAsync(string userId, Guid categoryId, Guid fieldId, CancellationToken ct = default)
    {
        var state = await RequireVaultAsync(userId, ct);
        RequireCategory(state, categoryId);
        var field = RequireField(state, categoryId, fieldId);

        var values = state.GetCategoryValues(categoryId);
        bool HasValue(FieldDefinition candidate) => values.TryGetValue(candidate.Id, out var v) && v is not null;

        var toDelete = new List<FieldDefinition> { field };

        if (field.FieldType == FieldType.Collection)
        {
            // A collection's data lives in its items, not in the category's
            // value bucket, so the "still holds data" check is the item count.
            var itemCount = state.ItemsOf(fieldId).Count;
            if (itemCount > 0)
            {
                throw new ConflictException(
                    $"'{field.Name}' still has {itemCount} item(s). Remove them before deleting it.");
            }

            toDelete.AddRange(state.ChildrenOf(fieldId));
        }
        else if (field.FieldType == FieldType.Group)
        {
            var children = state.ChildrenOf(fieldId);
            if (!CategorySchemaGuard.CanCascadeDeleteGroup(children, HasValue, out var blockingChild))
            {
                throw new ConflictException(
                    $"Cannot delete group '{field.Name}': child field '{blockingChild!.Name}' has a value.");
            }

            toDelete.AddRange(children);
        }
        else if (!CategorySchemaGuard.CanDeleteField(field, HasValue(field)))
        {
            throw new ConflictException($"Field '{field.Name}' cannot be deleted while it has a value.");
        }

        var events = toDelete
            .Select(f => (DomainEvent)new FieldDefinitionDeleted { VaultId = userId, FieldDefinitionId = f.Id })
            .ToList();

        // Removing a Group's last sub-field demotes it back to a plain Text
        // field - the mirror of the promotion in CreateFieldAsync - so it
        // renders as an editable field again rather than an empty container. A
        // Collection keeps its type: an empty collection is still a collection,
        // waiting for the user to add the next item.
        if (field.ParentFieldDefinitionId is { } parentId
            && state.FieldDefinitions.TryGetValue(parentId, out var parent)
            && parent.FieldType == FieldType.Group
            && state.ChildrenOf(parentId).Count == 1)
        {
            events.Add(new FieldDefinitionUpdated
            {
                VaultId = userId,
                FieldDefinitionId = parent.Id,
                NewFieldType = FieldType.Text
            });
        }

        await _repository.AppendAsync(userId, state.Version, events, ct);
    }

    private async Task<VaultState> RequireVaultAsync(string userId, CancellationToken ct)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        if (!state.Exists)
        {
            throw new NotFoundException($"No vault found for user '{userId}'.");
        }

        return state;
    }

    private static Category RequireCategory(VaultState state, Guid categoryId) =>
        state.Categories.TryGetValue(categoryId, out var category)
            ? category
            : throw new NotFoundException($"Category '{categoryId}' not found.");

    private static FieldDefinition RequireField(VaultState state, Guid categoryId, Guid fieldId)
    {
        if (!state.FieldDefinitions.TryGetValue(fieldId, out var field) || field.CategoryId != categoryId)
        {
            throw new NotFoundException($"Field '{fieldId}' not found in category '{categoryId}'.");
        }

        return field;
    }
}

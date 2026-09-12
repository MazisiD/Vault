using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Domain.Models;

namespace VaultID.Application.Services;

/// <summary>
/// Exposes a vault's category/field schema (dynamic-categories spec) as
/// read-only data so the presentation layer can render forms without
/// hard-coding categories or fields. Unlike the old compiled-in catalog, the
/// schema is now per-vault: it includes the 3 seeded system categories plus
/// any categories/fields the user has since created.
/// </summary>
public sealed class CategoryMetadataService(VaultStreamRepository repository)
{
    private readonly VaultStreamRepository _repository = repository;

    public async Task<IReadOnlyList<CategorySchemaView>> GetCategoriesAsync(string userId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        if (!state.Exists)
        {
            throw new NotFoundException($"No vault found for user '{userId}'.");
        }

        return state.Categories.Values
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CategorySchemaView(
                c.Id,
                c.Name,
                c.IsSystem,
                BuildFields(state, c.Id, parentFieldDefinitionId: null)))
            .ToList();
    }

    private static IReadOnlyList<FieldDefinitionView> BuildFields(VaultState state, Guid categoryId, Guid? parentFieldDefinitionId) =>
        state.FieldDefinitions.Values
            .Where(f => f.CategoryId == categoryId && f.ParentFieldDefinitionId == parentFieldDefinitionId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new FieldDefinitionView(
                f.Id,
                f.CategoryId,
                f.ParentFieldDefinitionId,
                f.Name,
                f.FieldType,
                f.AutocompleteToken,
                f.Choices,
                f.SortOrder,
                BuildFields(state, categoryId, f.Id)))
            .ToList();
}

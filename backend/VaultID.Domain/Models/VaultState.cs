using VaultID.Domain.Categories;

namespace VaultID.Domain.Models;

/// <summary>
/// The current state of a vault, rebuilt by replaying its event stream
/// (blueprint 4.3). This is a read model - it is never persisted directly;
/// it is always derived from events.
/// <para>
/// Since the dynamic-categories spec, the category/field schema itself is
/// part of this projected state (categories and field definitions can be
/// created/renamed/deleted like any other vault event), alongside the field
/// values it always held.
/// </para>
/// </summary>
public sealed class VaultState
{
    public required string UserId { get; init; }
    public string DisplayName { get; set; } = string.Empty;
    public bool Exists { get; set; }

    /// <summary>The version of the stream this state was built from.</summary>
    public long Version { get; set; }

    /// <summary>All categories currently defined on this vault, by id.</summary>
    public Dictionary<Guid, Category> Categories { get; } = new();

    /// <summary>All field definitions currently defined on this vault, by id.</summary>
    public Dictionary<Guid, FieldDefinition> FieldDefinitions { get; } = new();

    /// <summary>Current field values, keyed by category id then field definition id.</summary>
    public Dictionary<Guid, Dictionary<Guid, string?>> Values { get; } = new();

    /// <summary>All permission grants ever created, by id (active or not).</summary>
    public Dictionary<Guid, PermissionGrant> Grants { get; } = new();

    /// <summary>All share codes ever generated on this vault, by id.</summary>
    public Dictionary<Guid, ShareCode> ShareCodes { get; } = new();

    /// <summary>Latest agreement signed per organisation (agreement id).</summary>
    public Dictionary<string, string> SignedAgreements { get; } = new();

    /// <summary>Returns the live values of a category, or empty if none.</summary>
    public IReadOnlyDictionary<Guid, string?> GetCategoryValues(Guid categoryId) =>
        Values.TryGetValue(categoryId, out var values)
            ? values
            : new Dictionary<Guid, string?>();

    /// <summary>Returns a category's top-level field definitions (no parent), ordered.</summary>
    public IReadOnlyList<FieldDefinition> FieldsOf(Guid categoryId) =>
        FieldDefinitions.Values
            .Where(f => f.CategoryId == categoryId)
            .OrderBy(f => f.SortOrder)
            .ToList();

    /// <summary>Returns the direct children of a Group field, ordered.</summary>
    public IReadOnlyList<FieldDefinition> ChildrenOf(Guid parentFieldDefinitionId) =>
        FieldDefinitions.Values
            .Where(f => f.ParentFieldDefinitionId == parentFieldDefinitionId)
            .OrderBy(f => f.SortOrder)
            .ToList();

    /// <summary>Finds the active grant for an organisation + category, if any.</summary>
    public PermissionGrant? FindActiveGrant(string organisationId, Guid categoryId, DateTimeOffset now) =>
        Grants.Values.FirstOrDefault(g =>
            g.GranteeOrganisationId == organisationId &&
            g.CategoryId == categoryId &&
            g.IsCurrentlyActive(now));
}

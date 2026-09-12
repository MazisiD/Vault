namespace VaultID.Domain.Categories;

/// <summary>
/// The schema for one field of a category (dynamic-categories spec). A field
/// definition may be nested one level inside a <see cref="FieldType.Group"/>
/// field via <see cref="ParentFieldDefinitionId"/>. The actual value lives
/// separately, keyed by this definition's <see cref="Id"/> (see
/// <c>VaultState.Values</c>).
/// </summary>
public sealed class FieldDefinition
{
    public required Guid Id { get; init; }

    public required Guid CategoryId { get; init; }

    /// <summary>Null for a top-level field; set when nested inside a Group field.</summary>
    public Guid? ParentFieldDefinitionId { get; init; }

    public required string Name { get; set; }

    /// <summary>
    /// Settable because a scalar field is promoted to <see cref="FieldType.Group"/>
    /// the first time a sub-field is added under it (the new design's inline
    /// "add sub-field" affordance sits on every field row, not just Groups).
    /// </summary>
    public required FieldType FieldType { get; set; }

    /// <summary>
    /// The WHATWG HTML autocomplete token this field maps to, if any. Always
    /// one of <see cref="AutocompleteTokens.All"/> when set.
    /// </summary>
    public string? AutocompleteToken { get; init; }

    /// <summary>Only meaningful when <see cref="FieldType"/> is <see cref="FieldType.Choice"/>.</summary>
    public IReadOnlyList<string>? Choices { get; init; }

    public required int SortOrder { get; init; }
}

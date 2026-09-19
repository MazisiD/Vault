namespace VaultID.Domain.Categories;

/// <summary>
/// The schema for one field of a category (dynamic-categories spec). A field
/// definition may be nested one level inside a <see cref="FieldType.Group"/> or
/// <see cref="FieldType.Collection"/> field via
/// <see cref="ParentFieldDefinitionId"/>.
/// <para>
/// Where the value lives depends on the parent. A top-level field and a Group's
/// child both hold a single value keyed by this definition's <see cref="Id"/>
/// (see <c>VaultState.Values</c>). A Collection's child is a <em>template</em>:
/// it holds one value per collection item, keyed by the item's id and then by
/// this definition's <see cref="Id"/> (see <c>VaultState.ItemValues</c>).
/// </para>
/// </summary>
public sealed class FieldDefinition
{
    public required Guid Id { get; init; }

    public required Guid CategoryId { get; init; }

    /// <summary>Null for a top-level field; set when nested inside a Group or Collection field.</summary>
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
    public IReadOnlyList<string>? Choices { get; set; }

    /// <summary>
    /// True for a value the owner would not want shoulder-surfed - an ID
    /// number, an account number, a policy number. Presentation metadata only:
    /// the owner's own API responses still carry the value in full, because the
    /// owner is entitled to reveal it. It changes nothing about who else may
    /// read the field - that stays the permission engine's decision alone.
    /// </summary>
    public bool IsSecret { get; set; }

    /// <summary>
    /// Singular noun for one item of a <see cref="FieldType.Collection"/>, e.g.
    /// "bank account" for a "Bank accounts" collection. The UI uses it verbatim
    /// for its count badge and "add" affordance, because the noun is rarely
    /// derivable from the field name ("Insurance policies" -&gt; "policy",
    /// "Emergency contacts / next of kin" -&gt; "contact"). Null on anything
    /// that is not a Collection.
    /// </summary>
    public string? ItemNoun { get; set; }

    /// <summary>
    /// True on the one child of a <see cref="FieldType.Collection"/> whose value
    /// names an item in a collapsed list ("Capitec Bank", "Polo Vivo 2021").
    /// When no child is marked, the first child's value names the item.
    /// </summary>
    public bool IsItemTitle { get; init; }

    public required int SortOrder { get; init; }

    /// <summary>True for the two field types that never hold a value of their own.</summary>
    public bool IsContainer => FieldType is FieldType.Group or FieldType.Collection;
}

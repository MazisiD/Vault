namespace VaultID.Domain.Categories;

/// <summary>
/// Pure guardrail predicates for category/field-definition schema mutations
/// (dynamic-categories spec, "Mutation rules"). These are domain rules with no
/// I/O: the Application layer loads the relevant state, calls these, and turns
/// a "no" into the appropriate application exception.
/// </summary>
public static class CategorySchemaGuard
{
    /// <summary>System categories (the 3 seeded built-ins) can never be renamed.</summary>
    public static bool CanRename(Category category) => !category.IsSystem;

    /// <summary>
    /// A category can be deleted only if it is not a system category and it
    /// currently has no fields.
    /// </summary>
    public static bool CanDelete(Category category, bool hasFields) =>
        !category.IsSystem && !hasFields;

    /// <summary>
    /// A scalar field can be deleted only while it holds no value. A container
    /// (Group or Collection) is always deletable by itself - it holds no value
    /// of its own; what it contains is checked separately via
    /// <see cref="CanCascadeDeleteGroup"/>.
    /// </summary>
    public static bool CanDeleteField(FieldDefinition field, bool hasValue) =>
        field.IsContainer || !hasValue;

    /// <summary>
    /// A scalar field is promoted to a <see cref="FieldType.Group"/> the first
    /// time a sub-field is added under it. That is allowed only while the field
    /// holds no value of its own, because a container never carries a value -
    /// its children do - so promoting a populated field would silently orphan
    /// data. A field that is already a container needs no promotion; a File
    /// field can never become one.
    /// </summary>
    public static bool CanPromoteToGroup(FieldDefinition field, bool hasValue) =>
        field.FieldType != FieldType.File && !hasValue;

    /// <summary>
    /// A container field's delete cascades to its children only if every child
    /// is itself deletable (holds no value). Returns the first blocking child
    /// (if any) so the caller can report which field is holding data.
    /// </summary>
    public static bool CanCascadeDeleteGroup(
        IReadOnlyList<FieldDefinition> children,
        Func<FieldDefinition, bool> hasValue,
        out FieldDefinition? blockingChild)
    {
        blockingChild = children.FirstOrDefault(hasValue);
        return blockingChild is null;
    }
}

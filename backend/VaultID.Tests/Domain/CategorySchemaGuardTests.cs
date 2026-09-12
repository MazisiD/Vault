using VaultID.Domain;
using VaultID.Domain.Categories;
using Xunit;

namespace VaultID.Tests.Domain;

public sealed class CategorySchemaGuardTests
{
    private static Category MakeCategory(bool isSystem) =>
        new() { Id = Guid.NewGuid(), Name = "Sample", IsSystem = isSystem };

    private static FieldDefinition MakeField(Guid categoryId, FieldType type = FieldType.Text, Guid? parent = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            ParentFieldDefinitionId = parent,
            Name = "Field",
            FieldType = type,
            SortOrder = 0
        };

    [Fact]
    public void CanRename_ReturnsFalse_ForSystemCategory()
    {
        var category = MakeCategory(isSystem: true);
        Assert.False(CategorySchemaGuard.CanRename(category));
    }

    [Fact]
    public void CanRename_ReturnsTrue_ForCustomCategory()
    {
        var category = MakeCategory(isSystem: false);
        Assert.True(CategorySchemaGuard.CanRename(category));
    }

    [Fact]
    public void CanDelete_ReturnsFalse_ForSystemCategory_EvenWhenEmpty()
    {
        var category = MakeCategory(isSystem: true);
        Assert.False(CategorySchemaGuard.CanDelete(category, hasFields: false));
    }

    [Fact]
    public void CanDelete_ReturnsFalse_ForNonEmptyCustomCategory()
    {
        var category = MakeCategory(isSystem: false);
        Assert.False(CategorySchemaGuard.CanDelete(category, hasFields: true));
    }

    [Fact]
    public void CanDelete_ReturnsTrue_ForEmptyCustomCategory()
    {
        var category = MakeCategory(isSystem: false);
        Assert.True(CategorySchemaGuard.CanDelete(category, hasFields: false));
    }

    [Fact]
    public void CanDeleteField_ReturnsFalse_ForScalarFieldWithValue()
    {
        var field = MakeField(Guid.NewGuid());
        Assert.False(CategorySchemaGuard.CanDeleteField(field, hasValue: true));
    }

    [Fact]
    public void CanDeleteField_ReturnsTrue_ForScalarFieldWithoutValue()
    {
        var field = MakeField(Guid.NewGuid());
        Assert.True(CategorySchemaGuard.CanDeleteField(field, hasValue: false));
    }

    [Fact]
    public void CanDeleteField_ReturnsTrue_ForGroupField_RegardlessOfHasValueFlag()
    {
        var field = MakeField(Guid.NewGuid(), FieldType.Group);
        Assert.True(CategorySchemaGuard.CanDeleteField(field, hasValue: true));
    }

    [Fact]
    public void CanCascadeDeleteGroup_ReturnsTrue_WhenNoChildHasValue()
    {
        var categoryId = Guid.NewGuid();
        var children = new List<FieldDefinition> { MakeField(categoryId), MakeField(categoryId) };

        var result = CategorySchemaGuard.CanCascadeDeleteGroup(children, _ => false, out var blockingChild);

        Assert.True(result);
        Assert.Null(blockingChild);
    }

    [Fact]
    public void CanCascadeDeleteGroup_ReturnsFalse_AndNamesTheBlockingChild_WhenOneHasAValue()
    {
        var categoryId = Guid.NewGuid();
        var withValue = MakeField(categoryId);
        var children = new List<FieldDefinition> { MakeField(categoryId), withValue };

        var result = CategorySchemaGuard.CanCascadeDeleteGroup(children, f => f.Id == withValue.Id, out var blockingChild);

        Assert.False(result);
        Assert.Same(withValue, blockingChild);
    }
}

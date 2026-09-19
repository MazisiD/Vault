using VaultID.Domain;
using VaultID.Domain.Categories;
using Xunit;

namespace VaultID.Tests.Domain;

public sealed class FieldValueValidatorTests
{
    private static FieldDefinition MakeField(FieldType type, IReadOnlyList<string>? choices = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Name = "Field",
            FieldType = type,
            Choices = choices,
            SortOrder = 0
        };

    [Theory]
    [InlineData("42")]
    [InlineData("-3.5")]
    [InlineData("0")]
    public void Number_AcceptsParsableValues(string value)
    {
        Assert.True(FieldValueValidator.TryValidate(MakeField(FieldType.Number), value, out _));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData(null)]
    public void Number_RejectsUnparsableValues(string? value)
    {
        Assert.False(FieldValueValidator.TryValidate(MakeField(FieldType.Number), value, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Date_AcceptsParsableDate()
    {
        Assert.True(FieldValueValidator.TryValidate(MakeField(FieldType.Date), "2026-01-15", out _));
    }

    [Fact]
    public void Date_RejectsUnparsableValue()
    {
        Assert.False(FieldValueValidator.TryValidate(MakeField(FieldType.Date), "not-a-date", out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Boolean_AcceptsTrueOrFalse(string value)
    {
        Assert.True(FieldValueValidator.TryValidate(MakeField(FieldType.Boolean), value, out _));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("yes")]
    [InlineData(null)]
    public void Boolean_RejectsAnythingElse(string? value)
    {
        Assert.False(FieldValueValidator.TryValidate(MakeField(FieldType.Boolean), value, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Choice_AcceptsValueInList()
    {
        var field = MakeField(FieldType.Choice, ["Red", "Green", "Blue"]);
        Assert.True(FieldValueValidator.TryValidate(field, "Green", out _));
    }

    [Fact]
    public void Choice_RejectsValueNotInList()
    {
        var field = MakeField(FieldType.Choice, ["Red", "Green", "Blue"]);
        Assert.False(FieldValueValidator.TryValidate(field, "Purple", out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Choice_AllowsCustomValueWhenOtherIsOffered()
    {
        var field = MakeField(FieldType.Choice, ["Female", "Male", "Other"]);
        Assert.True(FieldValueValidator.TryValidate(field, "Non-binary", out _));
    }

    [Fact]
    public void Group_RejectsAnyValue()
    {
        var field = MakeField(FieldType.Group);
        Assert.False(FieldValueValidator.TryValidate(field, "anything", out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Group_AcceptsNullValue()
    {
        var field = MakeField(FieldType.Group);
        Assert.True(FieldValueValidator.TryValidate(field, null, out _));
    }

    [Theory]
    [InlineData(FieldType.Text)]
    [InlineData(FieldType.LongText)]
    [InlineData(FieldType.File)]
    public void FreeformTypes_AcceptAnyString(FieldType type)
    {
        Assert.True(FieldValueValidator.TryValidate(MakeField(type), "anything at all", out _));
    }
}

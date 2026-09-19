using VaultID.Domain;
using VaultID.Domain.Categories;
using Xunit;

namespace VaultID.Tests.Domain;

public sealed class CategoryCatalogTests
{
    [Fact]
    public void SeededBiographicalFields_UseTypedChoicesAndDateProperties()
    {
        var biographical = CategoryCatalog.SystemCategories.Single(c => c.Name == "Biographical");

        var dob = biographical.Fields.Single(f => f.Name == "DateOfBirth");
        Assert.Equal(FieldType.Date, dob.FieldType);

        var gender = biographical.Fields.Single(f => f.Name == "Gender");
        Assert.Equal(FieldType.Choice, gender.FieldType);
        Assert.Contains("Female", gender.Choices!);
        Assert.Contains("Male", gender.Choices!);
        Assert.Contains("Other", gender.Choices!);

        var nationality = biographical.Fields.Single(f => f.Name == "Nationality");
        Assert.Equal(FieldType.Choice, nationality.FieldType);
        Assert.Contains("South African", nationality.Choices!);
        Assert.Contains("Other", nationality.Choices!);

        var homeLanguage = biographical.Fields.Single(f => f.Name == "HomeLanguage");
        Assert.Equal(FieldType.Choice, homeLanguage.FieldType);
        Assert.Contains("English", homeLanguage.Choices!);
        Assert.Contains("Other", homeLanguage.Choices!);

        var marital = biographical.Fields.Single(f => f.Name == "MaritalStatus");
        Assert.Equal(FieldType.Choice, marital.FieldType);
        Assert.Contains("Single", marital.Choices!);
        Assert.Contains("Married", marital.Choices!);
        Assert.Contains("Other", marital.Choices!);
    }

    [Fact]
    public void SeededHealthFields_UseBloodTypeChoices()
    {
        var health = CategoryCatalog.SystemCategories.Single(c => c.Name == "Health");
        var bloodType = health.Fields.Single(f => f.Name == "BloodType");

        Assert.Equal(FieldType.Choice, bloodType.FieldType);
        Assert.Contains("A+", bloodType.Choices!);
        Assert.Contains("A-", bloodType.Choices!);
        Assert.Contains("B+", bloodType.Choices!);
        Assert.Contains("B-", bloodType.Choices!);
        Assert.Contains("AB+", bloodType.Choices!);
        Assert.Contains("AB-", bloodType.Choices!);
        Assert.Contains("O+", bloodType.Choices!);
        Assert.Contains("O-", bloodType.Choices!);
        Assert.Contains("Unknown", bloodType.Choices!);
    }
}

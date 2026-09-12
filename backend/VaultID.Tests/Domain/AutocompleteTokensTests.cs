using VaultID.Domain.Categories;
using Xunit;

namespace VaultID.Tests.Domain;

public sealed class AutocompleteTokensTests
{
    [Theory]
    [InlineData("email")]
    [InlineData("tel")]
    [InlineData("given-name")]
    [InlineData("family-name")]
    [InlineData("street-address")]
    [InlineData("bday")]
    [InlineData("postal-code")]
    [InlineData("country")]
    public void IsValid_ReturnsTrue_ForStandardTokens(string token)
    {
        Assert.True(AutocompleteTokens.IsValid(token));
    }

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("Email")] // case-sensitive per the WHATWG token grammar
    [InlineData("")]
    [InlineData("full-name")] // not a WHATWG token (the real token is "name")
    public void IsValid_ReturnsFalse_ForUnknownTokens(string token)
    {
        Assert.False(AutocompleteTokens.IsValid(token));
    }

    [Fact]
    public void All_ContainsNoDuplicates()
    {
        var distinctCount = AutocompleteTokens.All.Distinct().Count();
        Assert.Equal(AutocompleteTokens.All.Count, distinctCount);
    }
}

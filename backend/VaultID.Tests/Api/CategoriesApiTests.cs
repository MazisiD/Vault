using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using VaultID.Api.Contracts;
using VaultID.Application.Contracts;
using VaultID.Domain;
using Xunit;

namespace VaultID.Tests.Api;

/// <summary>
/// Integration tests for the dynamic-categories endpoints: vault creation
/// seeding, the metadata schema endpoint, category/field CRUD guardrails, and
/// the Guid-based UpdateField endpoint. Each test uses its own unique user id
/// so tests don't interfere with each other's (in-memory, process-lifetime)
/// vault state.
/// </summary>
public sealed class CategoriesApiTests : IClassFixture<VaultIdWebApplicationFactory>
{
    // Matches the server's JsonStringEnumConverter (Program.cs) so FieldType
    // values like "Text"/"Group" round-trip through the test HttpClient, which
    // otherwise uses System.Text.Json's numeric-enum default.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    // One authenticated identity per test instance (xUnit creates a fresh
    // instance per [Fact]), matching the "own vault per test" isolation the
    // tests already relied on when they generated their own Guid inline.
    private readonly string _userId = Guid.NewGuid().ToString("N");

    public CategoriesApiTests(VaultIdWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _userId);
    }

    private async Task<VaultSummary> CreateVaultAsync(string userId)
    {
        var response = await _client.PostAsJsonAsync("/api/vaults", new CreateVaultBody(userId, "Test User"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VaultSummary>(JsonOptions))!;
    }

    [Fact]
    public async Task CreatingAVault_SeedsTheSystemCategories()
    {
        var userId = _userId;
        var summary = await CreateVaultAsync(userId);

        Assert.Equal(5, summary.Categories.Count);
        Assert.All(summary.Categories, c => Assert.True(c.IsSystem));
        Assert.Contains(summary.Categories, c => c.Name == "Biographical");
        Assert.Contains(summary.Categories, c => c.Name == "Educational");
        Assert.Contains(summary.Categories, c => c.Name == "Financial");
        Assert.Contains(summary.Categories, c => c.Name == "Health");
        Assert.Contains(summary.Categories, c => c.Name == "Religious");
    }

    [Fact]
    public async Task MetadataEndpoint_ReturnsNestedSchemaWithFields()
    {
        var userId = _userId;
        await CreateVaultAsync(userId);

        var schema = await _client.GetFromJsonAsync<List<CategorySchemaView>>($"/api/vaults/{userId}/metadata/categories", JsonOptions);

        Assert.NotNull(schema);
        var biographical = schema!.Single(c => c.Name == "Biographical");
        Assert.Contains(biographical.Fields, f => f.Name == "FullName" && f.FieldType == FieldType.Text);
    }

    [Fact]
    public async Task CreateCategory_ThenAddAField_ThenReadItBackViaMetadata()
    {
        var userId = _userId;
        await CreateVaultAsync(userId);

        var createCategoryResponse = await _client.PostAsJsonAsync(
            $"/api/vaults/{userId}/categories", new CreateCategoryBody("Hobbies"));
        createCategoryResponse.EnsureSuccessStatusCode();
        var category = (await createCategoryResponse.Content.ReadFromJsonAsync<CategorySchemaView>(JsonOptions))!;

        Assert.False(category.IsSystem);

        var createFieldResponse = await _client.PostAsJsonAsync(
            $"/api/vaults/{userId}/categories/{category.Id}/fields",
            new CreateFieldBody("Favourite Sport", FieldType.Text, null, null, null));
        createFieldResponse.EnsureSuccessStatusCode();

        var schema = await _client.GetFromJsonAsync<List<CategorySchemaView>>($"/api/vaults/{userId}/metadata/categories", JsonOptions);
        var hobbies = schema!.Single(c => c.Id == category.Id);
        Assert.Contains(hobbies.Fields, f => f.Name == "Favourite Sport");
    }

    [Fact]
    public async Task RenamingASystemCategory_IsRejected()
    {
        var userId = _userId;
        var summary = await CreateVaultAsync(userId);
        var biographical = summary.Categories.First(c => c.Name == "Biographical");

        var response = await _client.PutAsJsonAsync(
            $"/api/vaults/{userId}/categories/{biographical.Id}", new RenameCategoryBody("Nope"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletingASystemCategory_IsRejected()
    {
        var userId = _userId;
        var summary = await CreateVaultAsync(userId);
        var biographical = summary.Categories.First(c => c.Name == "Biographical");

        var response = await _client.DeleteAsync($"/api/vaults/{userId}/categories/{biographical.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeletingAFieldWithAValue_IsRejected()
    {
        var userId = _userId;
        var summary = await CreateVaultAsync(userId);
        var biographical = summary.Categories.First(c => c.Name == "Biographical");

        var schema = await _client.GetFromJsonAsync<List<CategorySchemaView>>($"/api/vaults/{userId}/metadata/categories", JsonOptions);
        var fullName = schema!.Single(c => c.Id == biographical.Id).Fields.Single(f => f.Name == "FullName");

        var updateResponse = await _client.PutAsJsonAsync(
            $"/api/vaults/{userId}/fields/{fullName.Id}", new UpdateFieldBody("Jane Doe"));
        updateResponse.EnsureSuccessStatusCode();

        var deleteResponse = await _client.DeleteAsync($"/api/vaults/{userId}/categories/{biographical.Id}/fields/{fullName.Id}");

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateField_RejectsAnInvalidNumberValue()
    {
        var userId = _userId;
        await CreateVaultAsync(userId);

        var categoryResponse = await _client.PostAsJsonAsync(
            $"/api/vaults/{userId}/categories", new CreateCategoryBody("Finance"));
        var category = (await categoryResponse.Content.ReadFromJsonAsync<CategorySchemaView>(JsonOptions))!;

        var fieldResponse = await _client.PostAsJsonAsync(
            $"/api/vaults/{userId}/categories/{category.Id}/fields",
            new CreateFieldBody("Credit Score", FieldType.Number, null, null, null));
        var field = (await fieldResponse.Content.ReadFromJsonAsync<FieldDefinitionView>(JsonOptions))!;

        var response = await _client.PutAsJsonAsync(
            $"/api/vaults/{userId}/fields/{field.Id}", new UpdateFieldBody("not-a-number"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

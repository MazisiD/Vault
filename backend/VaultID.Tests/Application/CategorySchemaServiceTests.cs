using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Application.Services;
using VaultID.Domain;
using VaultID.Services.Abstractions;
using VaultID.Services.InMemory;
using Xunit;

namespace VaultID.Tests.Application;

/// <summary>
/// Schema-mutation tests for the affordances the vault screen exposes inline:
/// add a field by name only, add a sub-field under any field (which promotes
/// that field to a Group), and remove a field or sub-field (which demotes the
/// parent once its last child is gone).
/// </summary>
public sealed class CategorySchemaServiceTests
{
    private const string UserId = "user-1";

    private readonly VaultService _vaultService;
    private readonly CategorySchemaService _schemaService;
    private readonly CategoryMetadataService _metadataService;

    public CategorySchemaServiceTests()
    {
        IEventStore eventStore = new InMemoryEventStore();
        var repository = new VaultStreamRepository(eventStore, new EventSerializer());
        var notifications = new WebhookNotificationService(repository, new InMemoryWebhookSubscriptionStore());

        _vaultService = new VaultService(repository, notifications);
        _schemaService = new CategorySchemaService(repository);
        _metadataService = new CategoryMetadataService(repository);
    }

    private async Task<Guid> CreateVaultAndGetCategoryAsync()
    {
        var summary = await _vaultService.CreateVaultAsync(new CreateVaultRequest(UserId, "Jane"));
        return summary.Categories.First(c => c.Name == "Biographical").Id;
    }

    private async Task<FieldDefinitionView> ReadFieldAsync(Guid categoryId, Guid fieldId)
    {
        var schema = (await _metadataService.GetCategoriesAsync(UserId)).First(c => c.Id == categoryId);
        return schema.Fields.First(f => f.Id == fieldId);
    }

    [Fact]
    public async Task CreatingAFieldWithoutAType_DefaultsToText()
    {
        var categoryId = await CreateVaultAndGetCategoryAsync();

        var field = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Nickname", null, null, null, null));

        Assert.Equal(FieldType.Text, field.FieldType);
    }

    [Fact]
    public async Task AddingASubFieldUnderAPlainField_PromotesItToAGroup()
    {
        var categoryId = await CreateVaultAndGetCategoryAsync();

        var parent = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Contact details", null, null, null, null));
        Assert.Equal(FieldType.Text, parent.FieldType);

        var child = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Email", null, "email", null, parent.Id));

        var promoted = await ReadFieldAsync(categoryId, parent.Id);
        Assert.Equal(FieldType.Group, promoted.FieldType);
        Assert.Equal(child.Id, Assert.Single(promoted.Children).Id);
    }

    [Fact]
    public async Task AddingASubFieldUnderAPopulatedField_IsBlocked()
    {
        var categoryId = await CreateVaultAndGetCategoryAsync();

        var parent = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Contact details", null, null, null, null));
        await _vaultService.UpdateFieldAsync(UserId, new UpdateFieldRequest(parent.Id, "555-0100"));

        await Assert.ThrowsAsync<ConflictException>(() => _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Email", null, null, null, parent.Id)));
    }

    [Fact]
    public async Task AddingASubFieldUnderASubField_IsRejected()
    {
        var categoryId = await CreateVaultAndGetCategoryAsync();

        var parent = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Contact details", null, null, null, null));
        var child = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Email", null, null, null, parent.Id));

        await Assert.ThrowsAsync<ValidationException>(() => _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Work email", null, null, null, child.Id)));
    }

    [Fact]
    public async Task DeletingAGroupsLastSubField_DemotesItBackToText()
    {
        var categoryId = await CreateVaultAndGetCategoryAsync();

        var parent = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Contact details", null, null, null, null));
        var first = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Email", null, null, null, parent.Id));
        var second = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Cellphone", null, null, null, parent.Id));

        await _schemaService.DeleteFieldAsync(UserId, categoryId, first.Id);
        Assert.Equal(FieldType.Group, (await ReadFieldAsync(categoryId, parent.Id)).FieldType);

        await _schemaService.DeleteFieldAsync(UserId, categoryId, second.Id);

        var demoted = await ReadFieldAsync(categoryId, parent.Id);
        Assert.Equal(FieldType.Text, demoted.FieldType);
        Assert.Empty(demoted.Children);
    }

    [Fact]
    public async Task DeletingAField_RemovesItFromTheCategorySchema()
    {
        var categoryId = await CreateVaultAndGetCategoryAsync();

        var field = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Nickname", null, null, null, null));

        await _schemaService.DeleteFieldAsync(UserId, categoryId, field.Id);

        var schema = (await _metadataService.GetCategoriesAsync(UserId)).First(c => c.Id == categoryId);
        Assert.DoesNotContain(schema.Fields, f => f.Id == field.Id);
    }
}

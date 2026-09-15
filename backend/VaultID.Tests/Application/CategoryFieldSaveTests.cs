using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Application.Services;
using VaultID.Domain;
using VaultID.Domain.Events;
using VaultID.Services.Abstractions;
using VaultID.Services.InMemory;
using Xunit;

namespace VaultID.Tests.Application;

/// <summary>
/// The vault screen saves a whole category at once, so these cover the
/// guarantees that makes the batch safe: it is all-or-nothing, it still records
/// one event per field so the audit trail says exactly what changed, and a
/// value the user re-submitted unchanged writes nothing at all.
/// </summary>
public sealed class CategoryFieldSaveTests
{
    private const string UserId = "user-1";

    private readonly VaultService _vaultService;
    private readonly CategorySchemaService _schemaService;
    private readonly CategoryMetadataService _metadataService;
    private readonly VaultStreamRepository _repository;

    public CategoryFieldSaveTests()
    {
        IEventStore eventStore = new InMemoryEventStore();
        _repository = new VaultStreamRepository(eventStore, new EventSerializer());
        var notifications = new WebhookNotificationService(_repository, new InMemoryWebhookSubscriptionStore());

        _vaultService = new VaultService(_repository, notifications);
        _schemaService = new CategorySchemaService(_repository);
        _metadataService = new CategoryMetadataService(_repository);
    }

    private async Task<(Guid CategoryId, IReadOnlyList<FieldDefinitionView> Fields)> SeedAsync()
    {
        var summary = await _vaultService.CreateVaultAsync(new CreateVaultRequest(UserId, "Jane"));
        var categoryId = summary.Categories.First(c => c.Name == "Biographical").Id;
        var schema = (await _metadataService.GetCategoriesAsync(UserId)).First(c => c.Id == categoryId);
        return (categoryId, schema.Fields);
    }

    private async Task<IReadOnlyList<FieldUpdated>> FieldUpdatesAsync()
    {
        var events = await _repository.LoadEventsAsync(UserId);
        return events.OfType<FieldUpdated>().ToList();
    }

    [Fact]
    public async Task SavingACategory_RecordsOneEventPerChangedField()
    {
        var (categoryId, fields) = await SeedAsync();
        var first = fields[0];
        var second = fields[1];

        await _vaultService.UpdateCategoryFieldsAsync(UserId, new UpdateCategoryFieldsRequest(categoryId,
        [
            new FieldValueUpdate(first.Id, "Jane Doe"),
            new FieldValueUpdate(second.Id, "she/her")
        ]));

        var updates = await FieldUpdatesAsync();

        Assert.Equal(2, updates.Count);
        Assert.Contains(updates, u => u.FieldDefinitionId == first.Id && u.NewValue == "Jane Doe");
        Assert.Contains(updates, u => u.FieldDefinitionId == second.Id && u.NewValue == "she/her");
    }

    [Fact]
    public async Task SavingACategory_ReturnsTheStoredValues()
    {
        var (categoryId, fields) = await SeedAsync();

        var view = await _vaultService.UpdateCategoryFieldsAsync(UserId, new UpdateCategoryFieldsRequest(categoryId,
        [
            new FieldValueUpdate(fields[0].Id, "Jane Doe")
        ]));

        Assert.Equal("Jane Doe", view.Fields[fields[0].Id]);
    }

    [Fact]
    public async Task ResubmittingAnUnchangedValue_RecordsNothing()
    {
        var (categoryId, fields) = await SeedAsync();

        var request = new UpdateCategoryFieldsRequest(categoryId, [new FieldValueUpdate(fields[0].Id, "Jane Doe")]);
        await _vaultService.UpdateCategoryFieldsAsync(UserId, request);
        await _vaultService.UpdateCategoryFieldsAsync(UserId, request);

        Assert.Single(await FieldUpdatesAsync());
    }

    [Fact]
    public async Task OneInvalidValue_RejectsTheWholeSave()
    {
        var (categoryId, fields) = await SeedAsync();
        var birthday = await _schemaService.CreateFieldAsync(
            UserId, categoryId, new CreateFieldRequest("Birthday", FieldType.Date, null, null, null));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _vaultService.UpdateCategoryFieldsAsync(UserId, new UpdateCategoryFieldsRequest(categoryId,
            [
                new FieldValueUpdate(fields[0].Id, "Jane Doe"),
                new FieldValueUpdate(birthday.Id, "not-a-date")
            ])));

        // The valid value in the same batch must not have been written either.
        Assert.Empty(await FieldUpdatesAsync());
    }

    [Fact]
    public async Task SavingAFieldFromAnotherCategory_IsRejected()
    {
        await _vaultService.CreateVaultAsync(new CreateVaultRequest(UserId, "Jane"));
        var schema = await _metadataService.GetCategoriesAsync(UserId);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _vaultService.UpdateCategoryFieldsAsync(UserId, new UpdateCategoryFieldsRequest(schema[0].Id,
            [
                new FieldValueUpdate(schema[1].Fields[0].Id, "anything")
            ])));
    }

    [Fact]
    public async Task SavingIntoAMissingCategory_IsNotFound()
    {
        var (_, fields) = await SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _vaultService.UpdateCategoryFieldsAsync(UserId, new UpdateCategoryFieldsRequest(Guid.NewGuid(),
            [
                new FieldValueUpdate(fields[0].Id, "Jane Doe")
            ])));
    }
}

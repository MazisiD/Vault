using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Application.Permissions;
using VaultID.Application.Services;
using VaultID.Domain;
using VaultID.Services.Abstractions;
using VaultID.Services.InMemory;
using Xunit;

namespace VaultID.Tests.Application;

/// <summary>
/// End-to-end Application-layer tests for CategoryId-based sharing
/// (dynamic-categories spec): sharing a category by its Guid id, and that
/// sharing a category exposes the values of its nested Group children to the
/// organisation, since a Group's children share the parent's CategoryId.
/// </summary>
public sealed class CategorySharingTests
{
    private const string UserId = "user-1";

    private readonly VaultStreamRepository _repository;
    private readonly VaultService _vaultService;
    private readonly CategorySchemaService _schemaService;
    private readonly CategoryMetadataService _metadataService;
    private readonly SharingService _sharingService;
    private readonly OrganisationDataAccessService _dataAccess;
    private readonly OrganisationRegistryService _organisations;

    private int _orgCounter;

    public CategorySharingTests()
    {
        IEventStore eventStore = new InMemoryEventStore();
        var serializer = new EventSerializer();
        _repository = new VaultStreamRepository(eventStore, serializer);

        var subscriptions = new InMemoryWebhookSubscriptionStore();
        var notifications = new WebhookNotificationService(_repository, subscriptions);
        _vaultService = new VaultService(_repository, notifications);
        _schemaService = new CategorySchemaService(_repository);
        _metadataService = new CategoryMetadataService(_repository);

        _organisations = new OrganisationRegistryService(new InMemoryOrganisationStore());
        _sharingService = new SharingService(_repository, _organisations);
        _dataAccess = new OrganisationDataAccessService(_repository, new PermissionEngine());
    }

    private async Task<string> RegisterFreshOrgAsync()
    {
        var org = await _organisations.RegisterAsync(new RegisterOrganisationRequest(
            $"Org {++_orgCounter}", "Testing", 365, "Contractual obligation", null, null));
        return org.Id;
    }

    [Fact]
    public async Task SharingACategory_GrantsAccessToItsGroupChildFields()
    {
        var summary = await _vaultService.CreateVaultAsync(new CreateVaultRequest(UserId, "Jane"));
        var category = summary.Categories.First(c => c.Name == "Biographical");

        // Add a Group field with one nested child under the Biographical category.
        var group = await _schemaService.CreateFieldAsync(
            UserId, category.Id, new CreateFieldRequest("Contact details", FieldType.Group, null, null, null));
        var child = await _schemaService.CreateFieldAsync(
            UserId, category.Id, new CreateFieldRequest("Email", FieldType.Text, "email", null, group.Id));

        await _vaultService.UpdateFieldAsync(UserId, new UpdateFieldRequest(child.Id, "jane@example.com"));

        var orgId = await RegisterFreshOrgAsync();

        await _sharingService.ShareCategoryAsync(UserId, new ShareRequest(
            orgId, category.Id, AccessScope.ReadOnly, ShareDuration.ThirtyDays, ConsentMethod.InAppConfirmation));

        var result = await _dataAccess.QueryFieldAsync(UserId, orgId, child.Id, ipAddress: null);

        Assert.True(result.Allowed);
        Assert.Equal("jane@example.com", result.Fields[child.Id]);
    }

    [Fact]
    public async Task WithoutASharedGrant_AccessToAFieldIsDenied()
    {
        var summary = await _vaultService.CreateVaultAsync(new CreateVaultRequest(UserId, "Jane"));
        var category = summary.Categories.First(c => c.Name == "Health");

        var schema = (await _metadataService.GetCategoriesAsync(UserId)).First(c => c.Id == category.Id);
        var anyField = schema.Fields.First();

        var orgId = await RegisterFreshOrgAsync();

        var result = await _dataAccess.QueryFieldAsync(UserId, orgId, anyField.Id, ipAddress: null);

        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task RevokingAShare_DeniesFurtherAccess()
    {
        var summary = await _vaultService.CreateVaultAsync(new CreateVaultRequest(UserId, "Jane"));
        var category = summary.Categories.First(c => c.Name == "Educational");

        var orgId = await RegisterFreshOrgAsync();
        var grant = await _sharingService.ShareCategoryAsync(UserId, new ShareRequest(
            orgId, category.Id, AccessScope.ReadOnly, ShareDuration.ThirtyDays, ConsentMethod.InAppConfirmation));

        var beforeRevoke = await _dataAccess.QueryCategoryAsync(UserId, orgId, category.Id, ipAddress: null);
        Assert.True(beforeRevoke.Allowed);

        await _sharingService.RevokeAsync(UserId, grant.GrantId);

        var afterRevoke = await _dataAccess.QueryCategoryAsync(UserId, orgId, category.Id, ipAddress: null);
        Assert.False(afterRevoke.Allowed);
    }
}

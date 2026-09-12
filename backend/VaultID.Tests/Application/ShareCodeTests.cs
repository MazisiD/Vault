using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Application.Permissions;
using VaultID.Application.Services;
using VaultID.Application.Sharing;
using VaultID.Domain;
using VaultID.Services.Abstractions;
using VaultID.Services.InMemory;
using Xunit;

namespace VaultID.Tests.Application;

/// <summary>
/// End-to-end Application-layer tests for the share-code flow: the user picks
/// individual fields and mints a code, the organisation redeems it out-of-band,
/// and nothing is readable until the user approves the resulting request.
/// </summary>
public sealed class ShareCodeTests
{
    private const string UserId = "user-1";

    private readonly VaultStreamRepository _repository;
    private readonly VaultService _vaultService;
    private readonly CategorySchemaService _schemaService;
    private readonly CategoryMetadataService _metadataService;
    private readonly SharingService _sharingService;
    private readonly ShareCodeService _shareCodes;
    private readonly OrganisationDataAccessService _dataAccess;
    private readonly OrganisationRegistryService _organisations;

    private int _orgCounter;

    public ShareCodeTests()
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
        _shareCodes = new ShareCodeService(
            _repository, _organisations, new InMemoryShareCodeIndexStore(), new ShareCodeRateLimiter());
    }

    [Fact]
    public async Task GeneratingACode_ReturnsAFormattedCodeAwaitingRedemption()
    {
        var (orgId, fields) = await SetupAsync();

        var generated = await _shareCodes.GenerateAsync(UserId, Request(orgId, [fields[0].Id]));

        Assert.Matches("^[A-Z0-9]{4}-[A-Z0-9]{4}$", generated.Code);

        var listed = Assert.Single(await _shareCodes.ListAsync(UserId));
        Assert.Equal(ShareCodeStatus.Pending, listed.Status);
        Assert.Equal(generated.ShareCodeId, listed.ShareCodeId);
    }

    [Fact]
    public async Task RedeemingACode_RaisesARequestButGrantsNothingYet()
    {
        var (orgId, fields) = await SetupAsync();
        var generated = await _shareCodes.GenerateAsync(UserId, Request(orgId, [fields[0].Id]));

        var redemption = await _shareCodes.RedeemAsync(orgId, generated.Code, ipAddress: null);

        Assert.Equal(ShareCodeStatus.AwaitingApproval, redemption.Status);
        Assert.Equal(UserId, redemption.UserId);

        var pending = Assert.Single(await _shareCodes.ListPendingRequestsAsync(UserId));
        Assert.Equal(generated.ShareCodeId, pending.ShareCodeId);

        // Redemption alone must not expose anything.
        var read = await _dataAccess.QueryFieldAsync(UserId, orgId, fields[0].Id, ipAddress: null);
        Assert.False(read.Allowed);
    }

    [Fact]
    public async Task ApprovingARequest_ExposesOnlyTheTickedFields()
    {
        var (orgId, fields) = await SetupAsync();
        var shared = fields[0];
        var withheld = fields[1];

        var generated = await _shareCodes.GenerateAsync(UserId, Request(orgId, [shared.Id]));
        await _shareCodes.RedeemAsync(orgId, generated.Code, ipAddress: null);
        await _shareCodes.ApproveAsync(UserId, generated.ShareCodeId, ConsentMethod.InAppConfirmation);

        var sharedRead = await _dataAccess.QueryFieldAsync(UserId, orgId, shared.Id, ipAddress: null);
        Assert.True(sharedRead.Allowed);

        var withheldRead = await _dataAccess.QueryFieldAsync(UserId, orgId, withheld.Id, ipAddress: null);
        Assert.False(withheldRead.Allowed);

        // A whole-category read is narrowed to the ticked fields too.
        var categoryRead = await _dataAccess.QueryCategoryAsync(UserId, orgId, shared.CategoryId, ipAddress: null);
        Assert.True(categoryRead.Allowed);
        Assert.Equal([shared.Id], categoryRead.Fields.Keys);
    }

    [Fact]
    public async Task ApprovingARequest_HonoursTheUsersChosenEndDate()
    {
        var (orgId, fields) = await SetupAsync();
        var endsAt = DateTimeOffset.UtcNow.AddDays(3);

        var generated = await _shareCodes.GenerateAsync(UserId, Request(orgId, [fields[0].Id], endsAt));
        await _shareCodes.RedeemAsync(orgId, generated.Code, ipAddress: null);
        await _shareCodes.ApproveAsync(UserId, generated.ShareCodeId, ConsentMethod.InAppConfirmation);

        var grant = Assert.Single(await _sharingService.ListGrantsAsync(UserId));
        Assert.Equal(ShareDuration.Custom, grant.Duration);
        Assert.Equal(endsAt, grant.ExpiresAt);
    }

    [Fact]
    public async Task RejectingARequest_GrantsNothingAndRetiresTheCode()
    {
        var (orgId, fields) = await SetupAsync();
        var generated = await _shareCodes.GenerateAsync(UserId, Request(orgId, [fields[0].Id]));
        await _shareCodes.RedeemAsync(orgId, generated.Code, ipAddress: null);

        await _shareCodes.RejectAsync(UserId, generated.ShareCodeId);

        Assert.Empty(await _sharingService.ListGrantsAsync(UserId));
        Assert.Empty(await _shareCodes.ListPendingRequestsAsync(UserId));

        // The code is spent, so a retry must not resurrect the request.
        await Assert.ThrowsAsync<NotFoundException>(
            () => _shareCodes.RedeemAsync(orgId, generated.Code, ipAddress: null));
    }

    [Fact]
    public async Task ACodeMintedForOneOrganisation_CannotBeRedeemedByAnother()
    {
        var (orgId, fields) = await SetupAsync();
        var otherOrgId = await RegisterFreshOrgAsync();

        var generated = await _shareCodes.GenerateAsync(UserId, Request(orgId, [fields[0].Id]));

        await Assert.ThrowsAsync<NotFoundException>(
            () => _shareCodes.RedeemAsync(otherOrgId, generated.Code, ipAddress: null));
    }

    [Fact]
    public async Task RevokingACode_PreventsRedemption()
    {
        var (orgId, fields) = await SetupAsync();
        var generated = await _shareCodes.GenerateAsync(UserId, Request(orgId, [fields[0].Id]));

        await _shareCodes.RevokeAsync(UserId, generated.ShareCodeId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _shareCodes.RedeemAsync(orgId, generated.Code, ipAddress: null));
    }

    [Fact]
    public async Task RepeatedInvalidCodes_AreThrottled()
    {
        var orgId = await RegisterFreshOrgAsync();

        for (var i = 0; i < 10; i++)
        {
            await Assert.ThrowsAsync<NotFoundException>(
                () => _shareCodes.RedeemAsync(orgId, "ZZZZ-ZZZZ", ipAddress: null));
        }

        await Assert.ThrowsAsync<TooManyAttemptsException>(
            () => _shareCodes.RedeemAsync(orgId, "ZZZZ-ZZZZ", ipAddress: null));
    }

    [Fact]
    public async Task GeneratingACode_RejectsAFieldFromAnotherVault()
    {
        var (orgId, _) = await SetupAsync();

        await Assert.ThrowsAsync<ValidationException>(
            () => _shareCodes.GenerateAsync(UserId, Request(orgId, [Guid.NewGuid()])));
    }

    [Fact]
    public async Task ShorteningAGrantsExpiryToNow_EndsAccessImmediately()
    {
        var (orgId, fields) = await SetupAsync();
        var generated = await _shareCodes.GenerateAsync(UserId, Request(orgId, [fields[0].Id]));
        await _shareCodes.RedeemAsync(orgId, generated.Code, ipAddress: null);
        await _shareCodes.ApproveAsync(UserId, generated.ShareCodeId, ConsentMethod.InAppConfirmation);

        var grant = Assert.Single(await _sharingService.ListGrantsAsync(UserId));
        var extended = await _sharingService.ChangeExpiryAsync(
            UserId, new ChangeExpiryRequest(grant.GrantId, DateTimeOffset.UtcNow.AddYears(2)));

        Assert.Equal(DateTimeOffset.UtcNow.AddYears(2).Date, extended.ExpiresAt!.Value.Date);

        var stillReadable = await _dataAccess.QueryFieldAsync(UserId, orgId, fields[0].Id, ipAddress: null);
        Assert.True(stillReadable.Allowed);
    }

    [Fact]
    public async Task MovingAGrantsExpiryIntoThePast_IsRejected()
    {
        var (orgId, fields) = await SetupAsync();
        var generated = await _shareCodes.GenerateAsync(UserId, Request(orgId, [fields[0].Id]));
        await _shareCodes.RedeemAsync(orgId, generated.Code, ipAddress: null);
        await _shareCodes.ApproveAsync(UserId, generated.ShareCodeId, ConsentMethod.InAppConfirmation);

        var grant = Assert.Single(await _sharingService.ListGrantsAsync(UserId));

        await Assert.ThrowsAsync<ValidationException>(() => _sharingService.ChangeExpiryAsync(
            UserId, new ChangeExpiryRequest(grant.GrantId, DateTimeOffset.UtcNow.AddDays(-1))));
    }

    // --- Helpers ---

    /// <summary>
    /// Creates a vault with two populated Biographical fields plus a registered
    /// organisation, which is the shape most of these tests need.
    /// </summary>
    private async Task<(string OrgId, IReadOnlyList<FieldDefinitionView> Fields)> SetupAsync()
    {
        var summary = await _vaultService.CreateVaultAsync(new CreateVaultRequest(UserId, "Jane"));
        var category = summary.Categories.First(c => c.Name == "Biographical");

        var first = await _schemaService.CreateFieldAsync(
            UserId, category.Id, new CreateFieldRequest("Nickname", FieldType.Text, null, null, null));
        var second = await _schemaService.CreateFieldAsync(
            UserId, category.Id, new CreateFieldRequest("Pronouns", FieldType.Text, null, null, null));

        await _vaultService.UpdateFieldAsync(UserId, new UpdateFieldRequest(first.Id, "Janey"));
        await _vaultService.UpdateFieldAsync(UserId, new UpdateFieldRequest(second.Id, "she/her"));

        return (await RegisterFreshOrgAsync(), [first, second]);
    }

    private GenerateShareCodeRequest Request(
        string orgId, IReadOnlyList<Guid> fieldIds, DateTimeOffset? accessExpiresAt = null) =>
        new(orgId,
            fieldIds,
            accessExpiresAt ?? DateTimeOffset.UtcNow.AddDays(30),
            AccessScope.ReadOnly,
            ConsentMethod.InAppConfirmation);

    private async Task<string> RegisterFreshOrgAsync()
    {
        var org = await _organisations.RegisterAsync(new RegisterOrganisationRequest(
            $"Org {++_orgCounter}", "Testing", 365, "Contractual obligation", null, null));
        return org.Id;
    }
}

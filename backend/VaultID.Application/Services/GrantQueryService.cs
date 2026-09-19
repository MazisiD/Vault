using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Domain;
using VaultID.Domain.Events;
using VaultID.Services.Abstractions;

namespace VaultID.Application.Services;

/// <summary>
/// Cross-vault read model used by the organisation-facing API to list the
/// grants an organisation currently holds (blueprint endpoint GET /v1/grants).
/// <para>
/// It builds the projection by replaying the global event log. With a real
/// database this would be a maintained read model / materialised view rather
/// than an on-demand replay.
/// </para>
/// </summary>
public sealed class GrantQueryService(
    IEventStore eventStore,
    EventSerializer serializer,
    OrganisationRegistryService organisations)
{
    private readonly IEventStore _eventStore = eventStore;
    private readonly EventSerializer _serializer = serializer;
    private readonly OrganisationRegistryService _organisations = organisations;

    public async Task<IReadOnlyList<GrantView>> ListActiveForOrganisationAsync(string organisationId, CancellationToken ct = default)
    {
        // TODO: Replace the global replay with a maintained "grants" read model
        // (Supabase view/table) updated by an event subscriber.
        var stored = await _eventStore.ReadAllAsync(cancellationToken: ct);
        var now = DateTimeOffset.UtcNow;

        // GrantId -> (mutable projected grant fields)
        var grants = new Dictionary<Guid, ProjectedGrant>();
        var displayNames = new Dictionary<string, string>();

        foreach (var s in stored)
        {
            var e = _serializer.Deserialize(s);
            switch (e)
            {
                case VaultCreated vc:
                    displayNames[vc.VaultId] = vc.DisplayName;
                    break;
                case CategoryShared cs when cs.OrganisationId == organisationId:
                    grants[cs.GrantId] = new ProjectedGrant(
                        cs.GrantId, cs.VaultId, cs.OrganisationId, cs.CategoryId, cs.Scope,
                        cs.Duration, cs.AgreementId, cs.ExpiresAt, GrantStatus.Active, cs.OccurredAt,
                        cs.FieldDefinitionIds);
                    break;
                case ShareRevoked r when grants.ContainsKey(r.GrantId):
                    grants[r.GrantId] = grants[r.GrantId] with { Status = GrantStatus.Revoked };
                    break;
                case ShareExpired x when grants.ContainsKey(x.GrantId):
                    grants[x.GrantId] = grants[x.GrantId] with { Status = GrantStatus.Expired };
                    break;
                case ConsentRenewed cr when grants.ContainsKey(cr.GrantId):
                    grants[cr.GrantId] = grants[cr.GrantId] with { Status = GrantStatus.Active, ExpiresAt = cr.NewExpiresAt };
                    break;
                case ShareExpiryChanged ec when grants.ContainsKey(ec.GrantId):
                    grants[ec.GrantId] = grants[ec.GrantId] with { Status = GrantStatus.Active, ExpiresAt = ec.NewExpiresAt };
                    break;
            }
        }

        var orgName = await SafeOrgName(organisationId, ct);

        return grants.Values
            .Where(g => g.Status == GrantStatus.Active && (g.ExpiresAt is null || g.ExpiresAt > now))
            .OrderByDescending(g => g.ConsentedAt)
            .Select(g => new GrantView(
                g.GrantId, g.UserId, displayNames.GetValueOrDefault(g.UserId, g.UserId), g.OrganisationId, orgName,
                g.CategoryId, g.Scope, g.Duration, g.AgreementId, g.ExpiresAt, g.Status, g.ConsentedAt,
                g.FieldDefinitionIds))
            .ToList();
    }

    /// <summary>
    /// Lists every share-code request this organisation has ever redeemed, with
    /// its current status, so the organisation can see requests it is still
    /// awaiting a decision on (or that were rejected/expired/revoked) alongside
    /// the ones that were approved.
    /// </summary>
    public async Task<IReadOnlyList<OrganisationShareRequestView>> ListShareRequestsForOrganisationAsync(
        string organisationId, CancellationToken ct = default)
    {
        var stored = await _eventStore.ReadAllAsync(cancellationToken: ct);

        var displayNames = new Dictionary<string, string>();
        var fieldCounts = new Dictionary<Guid, int>();
        var accessExpiries = new Dictionary<Guid, DateTimeOffset>();
        var requests = new Dictionary<Guid, ProjectedShareRequest>();

        foreach (var s in stored)
        {
            var e = _serializer.Deserialize(s);
            switch (e)
            {
                case VaultCreated vc:
                    displayNames[vc.VaultId] = vc.DisplayName;
                    break;
                case ShareCodeGenerated g:
                    fieldCounts[g.ShareCodeId] = g.FieldDefinitionIds.Count;
                    accessExpiries[g.ShareCodeId] = g.AccessExpiresAt;
                    break;
                case ShareCodeRedeemed r when r.OrganisationId == organisationId:
                    requests[r.ShareCodeId] = new ProjectedShareRequest(
                        r.ShareCodeId, r.VaultId, r.OrganisationId, ShareCodeStatus.AwaitingApproval, r.OccurredAt);
                    break;
                case ShareCodeApproved a when requests.ContainsKey(a.ShareCodeId):
                    requests[a.ShareCodeId] = requests[a.ShareCodeId] with { Status = ShareCodeStatus.Approved };
                    break;
                case ShareCodeRejected rj when requests.ContainsKey(rj.ShareCodeId):
                    requests[rj.ShareCodeId] = requests[rj.ShareCodeId] with { Status = ShareCodeStatus.Rejected };
                    break;
                case ShareCodeRevoked rv when requests.ContainsKey(rv.ShareCodeId):
                    requests[rv.ShareCodeId] = requests[rv.ShareCodeId] with { Status = ShareCodeStatus.Revoked };
                    break;
                case ShareCodeExpired ex when requests.ContainsKey(ex.ShareCodeId):
                    requests[ex.ShareCodeId] = requests[ex.ShareCodeId] with { Status = ShareCodeStatus.Expired };
                    break;
            }
        }

        return requests.Values
            .OrderByDescending(r => r.RequestedAt)
            .Select(r => new OrganisationShareRequestView(
                r.ShareCodeId,
                r.UserId,
                displayNames.GetValueOrDefault(r.UserId, r.UserId),
                r.Status,
                fieldCounts.GetValueOrDefault(r.ShareCodeId, 0),
                r.RequestedAt,
                accessExpiries.GetValueOrDefault(r.ShareCodeId, r.RequestedAt)))
            .ToList();
    }

    private async Task<string> SafeOrgName(string organisationId, CancellationToken ct)
    {
        try
        {
            return (await _organisations.GetAsync(organisationId, ct)).Name;
        }
        catch
        {
            return organisationId;
        }
    }

    private sealed record ProjectedGrant(
        Guid GrantId,
        string UserId,
        string OrganisationId,
        Guid CategoryId,
        AccessScope Scope,
        ShareDuration Duration,
        string AgreementId,
        DateTimeOffset? ExpiresAt,
        GrantStatus Status,
        DateTimeOffset ConsentedAt,
        IReadOnlyList<Guid>? FieldDefinitionIds);

    private sealed record ProjectedShareRequest(
        Guid ShareCodeId,
        string UserId,
        string OrganisationId,
        ShareCodeStatus Status,
        DateTimeOffset RequestedAt);
}

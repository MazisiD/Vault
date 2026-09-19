using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Application.Sharing;
using VaultID.Domain;
using VaultID.Domain.Events;
using VaultID.Domain.Models;

namespace VaultID.Application.Services;

/// <summary>
/// Sharing, consent, revocation and expiry use cases (blueprint 5.2, 5.4, 5.7).
/// This service owns the consent flow: the user must accept the org's DPA, then
/// a time-bound permission grant is created.
/// </summary>
public sealed class SharingService(
    VaultStreamRepository repository,
    OrganisationRegistryService organisations)
{
    private readonly VaultStreamRepository _repository = repository;
    private readonly OrganisationRegistryService _organisations = organisations;

    /// <summary>
    /// Shares a category with an organisation (blueprint 5.2). Records both the
    /// AgreementSigned consent and the CategoryShared grant as immutable events.
    /// </summary>
    public async Task<GrantView> ShareCategoryAsync(string userId, ShareRequest request, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        if (!state.Exists)
        {
            throw new NotFoundException($"No vault found for user '{userId}'.");
        }

        if (!state.Categories.ContainsKey(request.CategoryId))
        {
            throw new ValidationException($"Vault has no category '{request.CategoryId}'.");
        }

        // The organisation must have a (compliance-approved) DPA to present.
        var agreement = await _organisations.GetAgreementAsync(request.OrganisationId, ct);
        var org = await _organisations.GetAsync(request.OrganisationId, ct);
        if (org.Status is not "approved")
        {
            throw new ConflictException($"Organisation '{org.Name}' is not approved to receive data (status: {org.Status}).");
        }

        var now = DateTimeOffset.UtcNow;
        var expiry = SharePolicy.ComputeExpiry(request.Duration, now);
        var grantId = Guid.NewGuid();

        var signed = new AgreementSigned
        {
            VaultId = userId,
            OrganisationId = request.OrganisationId,
            AgreementId = agreement.AgreementId,
            AgreementHash = OrganisationRegistryService.ComputeAgreementHash(agreement),
            ConsentMethod = request.ConsentMethod
        };

        var shared = new CategoryShared
        {
            VaultId = userId,
            GrantId = grantId,
            OrganisationId = request.OrganisationId,
            CategoryId = request.CategoryId,
            Scope = request.Scope,
            Duration = request.Duration,
            AgreementId = agreement.AgreementId,
            ExpiresAt = expiry
        };

        await _repository.AppendAsync(userId, state.Version, [signed, shared], ct);

        return new GrantView(
            grantId,
            userId,
            request.OrganisationId,
            org.Name,
            request.CategoryId,
            request.Scope,
            request.Duration,
            agreement.AgreementId,
            expiry,
            GrantStatus.Active,
            now,
            null);
    }

    /// <summary>Instantly revokes a share (blueprint 5.4).</summary>
    public async Task RevokeAsync(string userId, Guid grantId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var grant = RequireGrant(state, grantId);

        if (grant.Status is GrantStatus.Revoked)
        {
            return; // idempotent
        }

        var revoked = new ShareRevoked
        {
            VaultId = userId,
            GrantId = grant.Id,
            OrganisationId = grant.GranteeOrganisationId,
            CategoryId = grant.CategoryId
        };

        await _repository.AppendAsync(userId, state.Version, [revoked], ct);
    }

    /// <summary>Renews a share with a new duration (blueprint 5.7).</summary>
    public async Task<GrantView> RenewAsync(string userId, RenewShareRequest request, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var grant = RequireGrant(state, request.GrantId);

        if (grant.Status is GrantStatus.Revoked)
        {
            throw new ConflictException("Cannot renew a revoked share.");
        }

        var now = DateTimeOffset.UtcNow;
        var newExpiry = SharePolicy.ComputeExpiry(request.NewDuration, now);

        var renewed = new ConsentRenewed
        {
            VaultId = userId,
            GrantId = grant.Id,
            OrganisationId = grant.GranteeOrganisationId,
            CategoryId = grant.CategoryId,
            NewDuration = request.NewDuration,
            NewExpiresAt = newExpiry
        };

        await _repository.AppendAsync(userId, state.Version, [renewed], ct);

        var newState = await _repository.LoadStateAsync(userId, ct);
        return await ToViewAsync(newState.Grants[grant.Id], ct);
    }

    /// <summary>
    /// Moves an existing share's end date to an exact instant the user picked -
    /// extending or shortening it. This is the counterpart to the share-code
    /// flow, where the user chooses a concrete expiry rather than a preset
    /// duration and stays free to change it afterwards.
    /// </summary>
    public async Task<GrantView> ChangeExpiryAsync(string userId, ChangeExpiryRequest request, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var grant = RequireGrant(state, request.GrantId);

        if (grant.Status is GrantStatus.Revoked)
        {
            throw new ConflictException("Cannot reschedule a revoked share. Share again to restore access.");
        }

        if (request.NewExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new ValidationException("The new expiry must be in the future. Revoke the share to end it now.");
        }

        var changed = new ShareExpiryChanged
        {
            VaultId = userId,
            GrantId = grant.Id,
            OrganisationId = grant.GranteeOrganisationId,
            CategoryId = grant.CategoryId,
            NewExpiresAt = request.NewExpiresAt
        };

        await _repository.AppendAsync(userId, state.Version, [changed], ct);

        var newState = await _repository.LoadStateAsync(userId, ct);
        return await ToViewAsync(newState.Grants[grant.Id], ct);
    }

    /// <summary>Lists all grants for a user, newest first, with org names.</summary>
    public async Task<IReadOnlyList<GrantView>> ListGrantsAsync(string userId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var views = new List<GrantView>();
        foreach (var grant in state.Grants.Values.OrderByDescending(g => g.ConsentedAt))
        {
            views.Add(await ToViewAsync(grant, ct));
        }

        return views;
    }

    /// <summary>
    /// Sweeps a vault's grants, emitting RenewalRequested when within the notice
    /// window and ShareExpired when the duration has fully elapsed (blueprint
    /// 4.6, 5.7). Intended to be run by a scheduled job.
    /// </summary>
    public async Task ProcessExpiriesAsync(string userId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;
        var newEvents = new List<DomainEvent>();

        foreach (var grant in state.Grants.Values)
        {
            if (grant.ExpiresAt is null)
            {
                continue; // indefinite - handled by periodic re-consent elsewhere
            }

            if (grant.Status is GrantStatus.Active && grant.ExpiresAt <= now)
            {
                newEvents.Add(new ShareExpired
                {
                    VaultId = userId,
                    GrantId = grant.Id,
                    OrganisationId = grant.GranteeOrganisationId,
                    CategoryId = grant.CategoryId,
                    OriginalDuration = grant.Duration
                });
            }
            else if (grant.Status is GrantStatus.Active)
            {
                var noticeDays = SharePolicy.RenewalNoticeDays(grant.Duration);
                var daysRemaining = (int)Math.Ceiling((grant.ExpiresAt.Value - now).TotalDays);
                if (noticeDays > 0 && daysRemaining <= noticeDays)
                {
                    newEvents.Add(new RenewalRequested
                    {
                        VaultId = userId,
                        GrantId = grant.Id,
                        OrganisationId = grant.GranteeOrganisationId,
                        CategoryId = grant.CategoryId,
                        DaysRemaining = Math.Max(daysRemaining, 0)
                    });
                }
            }
        }

        // Share codes that nobody acted on inside their window are closed off
        // here too, so a stale code can never be redeemed later.
        foreach (var code in state.ShareCodes.Values)
        {
            if (code.Status is ShareCodeStatus.Pending or ShareCodeStatus.AwaitingApproval &&
                code.CodeExpiresAt <= now)
            {
                newEvents.Add(new ShareCodeExpired
                {
                    VaultId = userId,
                    ShareCodeId = code.Id,
                    OrganisationId = code.OrganisationId
                });
            }
        }

        if (newEvents.Count > 0)
        {
            await _repository.AppendAsync(userId, state.Version, newEvents, ct);
        }
    }

    private static PermissionGrant RequireGrant(VaultState state, Guid grantId) =>
        state.Grants.TryGetValue(grantId, out var grant)
            ? grant
            : throw new NotFoundException($"Grant '{grantId}' not found.");

    private async Task<GrantView> ToViewAsync(PermissionGrant grant, CancellationToken ct)
    {
        string orgName;
        try
        {
            orgName = (await _organisations.GetAsync(grant.GranteeOrganisationId, ct)).Name;
        }
        catch (NotFoundException)
        {
            orgName = grant.GranteeOrganisationId;
        }

        return new GrantView(
            grant.Id,
            grant.GrantorUserId,
            grant.GranteeOrganisationId,
            orgName,
            grant.CategoryId,
            grant.Scope,
            grant.Duration,
            grant.AgreementId,
            grant.ExpiresAt,
            grant.Status,
            grant.ConsentedAt,
            grant.FieldDefinitionIds);
    }
}

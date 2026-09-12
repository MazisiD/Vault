using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Application.Sharing;
using VaultID.Domain;
using VaultID.Domain.Events;
using VaultID.Domain.Models;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Application.Services;

/// <summary>
/// The share-code flow: the user picks exactly which fields an organisation may
/// see and mints a one-time code for it; the organisation redeems that code to
/// raise a request; the user reviews the organisation's data-processing
/// agreement and approves or rejects it. Only approval creates grants.
/// <para>
/// Consent is deliberately split in two. Generating a code says "I am willing to
/// share these fields with this organisation"; approving after redemption says
/// "I have read what you will do with them". Redemption in between grants
/// nothing at all, so a leaked code cannot by itself expose any data.
/// </para>
/// </summary>
public sealed class ShareCodeService(
    VaultStreamRepository repository,
    OrganisationRegistryService organisations,
    IShareCodeIndexStore codeIndex,
    ShareCodeRateLimiter rateLimiter)
{
    private readonly VaultStreamRepository _repository = repository;
    private readonly OrganisationRegistryService _organisations = organisations;
    private readonly IShareCodeIndexStore _codeIndex = codeIndex;
    private readonly ShareCodeRateLimiter _rateLimiter = rateLimiter;

    // --- User-facing use cases ---

    /// <summary>
    /// Mints a code for one organisation over the selected fields. The returned
    /// plaintext code is the only copy that will ever exist.
    /// </summary>
    public async Task<GeneratedShareCodeView> GenerateAsync(
        string userId, GenerateShareCodeRequest request, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        if (!state.Exists)
        {
            throw new NotFoundException($"No vault found for user '{userId}'.");
        }

        if (request.FieldDefinitionIds is null || request.FieldDefinitionIds.Count == 0)
        {
            throw new ValidationException("Select at least one field to share.");
        }

        // Deduplicate, and verify every field actually belongs to this vault so
        // a caller cannot smuggle in another user's field ids.
        var fieldIds = request.FieldDefinitionIds.Distinct().ToList();
        var unknown = fieldIds.Where(id => !state.FieldDefinitions.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new ValidationException($"Vault has no field '{unknown[0]}'.");
        }

        var now = DateTimeOffset.UtcNow;
        if (request.AccessExpiresAt <= now)
        {
            throw new ValidationException("The access expiry must be in the future.");
        }

        var org = await _organisations.GetAsync(request.OrganisationId, ct);
        if (org.Status is not "approved")
        {
            throw new ConflictException($"Organisation '{org.Name}' is not approved to receive data (status: {org.Status}).");
        }

        // Presenting the agreement is part of the audit trail even though the
        // user only reads it at approval time.
        _ = await _organisations.GetAgreementAsync(request.OrganisationId, ct);

        var code = ShareCodeGenerator.Generate();
        var codeHash = ShareCodeGenerator.Hash(code);
        var shareCodeId = Guid.NewGuid();
        var codeExpiresAt = now.Add(ShareCodeGenerator.CodeLifetime);

        var generated = new ShareCodeGenerated
        {
            VaultId = userId,
            ShareCodeId = shareCodeId,
            CodeHash = codeHash,
            OrganisationId = request.OrganisationId,
            FieldDefinitionIds = fieldIds,
            AccessExpiresAt = request.AccessExpiresAt,
            CodeExpiresAt = codeExpiresAt
        };

        await _repository.AppendAsync(userId, state.Version, [generated], ct);

        await _codeIndex.AddAsync(
            new ShareCodeIndexRecord
            {
                CodeHash = codeHash,
                UserId = userId,
                ShareCodeId = shareCodeId,
                OrganisationId = request.OrganisationId,
                ExpiresAt = codeExpiresAt
            },
            ct);

        return new GeneratedShareCodeView(
            shareCodeId, code, org.Id, org.Name, codeExpiresAt, request.AccessExpiresAt);
    }

    /// <summary>Lists every code the user has generated, newest first.</summary>
    public async Task<IReadOnlyList<ShareCodeView>> ListAsync(string userId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;

        var views = new List<ShareCodeView>();
        foreach (var code in state.ShareCodes.Values.OrderByDescending(c => c.CreatedAt))
        {
            views.Add(new ShareCodeView(
                code.Id,
                code.OrganisationId,
                await SafeOrgNameAsync(code.OrganisationId, ct),
                EffectiveStatus(code, now),
                DescribeFields(state, code.FieldDefinitionIds),
                code.AccessExpiresAt,
                code.CodeExpiresAt,
                code.CreatedAt,
                code.RedeemedAt));
        }

        return views;
    }

    /// <summary>
    /// Lists redeemed codes waiting on the user's decision, each bundled with the
    /// organisation's agreement so the user can read it before approving.
    /// </summary>
    public async Task<IReadOnlyList<PendingShareRequestView>> ListPendingRequestsAsync(
        string userId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;

        var views = new List<PendingShareRequestView>();
        foreach (var code in state.ShareCodes.Values
                     .Where(c => c.IsAwaitingApproval(now))
                     .OrderByDescending(c => c.RedeemedAt))
        {
            AgreementView agreement;
            try
            {
                agreement = await _organisations.GetAgreementAsync(code.OrganisationId, ct);
            }
            catch (Exception e) when (e is NotFoundException or ConflictException)
            {
                // An org that lost its agreement can no longer be approved; the
                // request simply stops being offered rather than blocking the page.
                continue;
            }

            views.Add(new PendingShareRequestView(
                code.Id,
                code.OrganisationId,
                agreement.OrganisationName,
                DescribeFields(state, code.FieldDefinitionIds),
                code.AccessExpiresAt,
                code.RedeemedAt ?? code.CreatedAt,
                agreement));
        }

        return views;
    }

    /// <summary>
    /// The user accepts the organisation's agreement and releases the fields
    /// they pre-selected. One grant is created per category the selection spans,
    /// each restricted to exactly the ticked fields.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ApproveAsync(
        string userId, Guid shareCodeId, ConsentMethod consentMethod, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var code = RequireCode(state, shareCodeId);
        var now = DateTimeOffset.UtcNow;

        if (!code.IsAwaitingApproval(now))
        {
            throw new ConflictException($"This request is not awaiting approval (status: {EffectiveStatus(code, now)}).");
        }

        var agreement = await _organisations.GetAgreementAsync(code.OrganisationId, ct);

        // The expiry the user chose at generation time may have slipped into the
        // past while the organisation sat on the code.
        if (code.AccessExpiresAt <= now)
        {
            throw new ConflictException("The access period you chose has already passed. Generate a new code.");
        }

        var events = new List<DomainEvent>
        {
            new AgreementSigned
            {
                VaultId = userId,
                OrganisationId = code.OrganisationId,
                AgreementId = agreement.AgreementId,
                AgreementHash = OrganisationRegistryService.ComputeAgreementHash(agreement),
                ConsentMethod = consentMethod
            }
        };

        var grantIds = new List<Guid>();
        foreach (var group in GroupByCategory(state, code.FieldDefinitionIds))
        {
            var grantId = Guid.NewGuid();
            grantIds.Add(grantId);

            events.Add(new CategoryShared
            {
                VaultId = userId,
                GrantId = grantId,
                OrganisationId = code.OrganisationId,
                CategoryId = group.Key,
                Scope = AccessScope.ReadOnly,
                Duration = ShareDuration.Custom,
                AgreementId = agreement.AgreementId,
                FieldDefinitionIds = group.Value,
                ShareCodeId = code.Id,
                ExpiresAt = code.AccessExpiresAt
            });
        }

        if (grantIds.Count == 0)
        {
            throw new ConflictException("The fields this request covers no longer exist in your vault.");
        }

        events.Add(new ShareCodeApproved
        {
            VaultId = userId,
            ShareCodeId = code.Id,
            OrganisationId = code.OrganisationId,
            GrantIds = grantIds
        });

        await _repository.AppendAsync(userId, state.Version, events, ct);
        await _codeIndex.RemoveAsync(code.CodeHash, ct);

        return grantIds;
    }

    /// <summary>The user declines the organisation's request. The code is spent.</summary>
    public async Task RejectAsync(string userId, Guid shareCodeId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var code = RequireCode(state, shareCodeId);
        var now = DateTimeOffset.UtcNow;

        if (!code.IsAwaitingApproval(now))
        {
            throw new ConflictException($"This request is not awaiting approval (status: {EffectiveStatus(code, now)}).");
        }

        var rejected = new ShareCodeRejected
        {
            VaultId = userId,
            ShareCodeId = code.Id,
            OrganisationId = code.OrganisationId
        };

        await _repository.AppendAsync(userId, state.Version, [rejected], ct);
        await _codeIndex.RemoveAsync(code.CodeHash, ct);
    }

    /// <summary>The user cancels a code before it has been acted on.</summary>
    public async Task RevokeAsync(string userId, Guid shareCodeId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var code = RequireCode(state, shareCodeId);

        if (code.Status is ShareCodeStatus.Revoked)
        {
            return; // idempotent
        }

        if (code.Status is ShareCodeStatus.Approved or ShareCodeStatus.Rejected)
        {
            throw new ConflictException("This code has already been used. Revoke the resulting share instead.");
        }

        var revoked = new ShareCodeRevoked
        {
            VaultId = userId,
            ShareCodeId = code.Id,
            OrganisationId = code.OrganisationId
        };

        await _repository.AppendAsync(userId, state.Version, [revoked], ct);
        await _codeIndex.RemoveAsync(code.CodeHash, ct);
    }

    // --- Organisation-facing use case ---

    /// <summary>
    /// An organisation presents a code. On success this raises a request for the
    /// user to approve and returns nothing but the fact that it is queued - no
    /// field values, and no access, until the user approves.
    /// <para>
    /// Every failure path returns the same <see cref="NotFoundException"/> text.
    /// Distinguishing "no such code" from "that code isn't yours" would let a
    /// caller confirm which codes exist, so the endpoint stays deliberately
    /// silent about why it refused.
    /// </para>
    /// </summary>
    public async Task<ShareCodeRedemptionView> RedeemAsync(
        string organisationId, string code, string? ipAddress, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organisationId);

        var now = DateTimeOffset.UtcNow;
        if (_rateLimiter.IsThrottled(organisationId, now))
        {
            throw new TooManyAttemptsException(
                "Too many invalid share codes. Wait a few minutes before trying again.");
        }

        if (!ShareCodeGenerator.IsWellFormed(code))
        {
            _rateLimiter.RecordFailure(organisationId, now);
            throw new NotFoundException(InvalidCodeMessage);
        }

        var record = await _codeIndex.FindByHashAsync(ShareCodeGenerator.Hash(code), ct);
        if (record is null || record.OrganisationId != organisationId || record.ExpiresAt <= now)
        {
            _rateLimiter.RecordFailure(organisationId, now);
            throw new NotFoundException(InvalidCodeMessage);
        }

        var state = await _repository.LoadStateAsync(record.UserId, ct);
        if (!state.ShareCodes.TryGetValue(record.ShareCodeId, out var shareCode) ||
            shareCode.OrganisationId != organisationId)
        {
            _rateLimiter.RecordFailure(organisationId, now);
            throw new NotFoundException(InvalidCodeMessage);
        }

        _rateLimiter.RecordSuccess(organisationId);

        // Redeeming again while the user is still deciding is a no-op rather
        // than an error, so an organisation that lost the response can retry.
        if (shareCode.IsAwaitingApproval(now))
        {
            return new ShareCodeRedemptionView(
                shareCode.Id,
                record.UserId,
                ShareCodeStatus.AwaitingApproval,
                shareCode.FieldDefinitionIds.Count,
                shareCode.AccessExpiresAt);
        }

        if (!shareCode.IsRedeemable(now))
        {
            throw new ConflictException($"This code can no longer be redeemed (status: {EffectiveStatus(shareCode, now)}).");
        }

        var redeemed = new ShareCodeRedeemed
        {
            VaultId = record.UserId,
            ShareCodeId = shareCode.Id,
            OrganisationId = organisationId,
            IpAddress = ipAddress
        };

        await _repository.AppendAsync(record.UserId, state.Version, [redeemed], ct);

        return new ShareCodeRedemptionView(
            shareCode.Id,
            record.UserId,
            ShareCodeStatus.AwaitingApproval,
            shareCode.FieldDefinitionIds.Count,
            shareCode.AccessExpiresAt);
    }

    // --- Helpers ---

    private const string InvalidCodeMessage = "That share code is not valid.";

    private static ShareCode RequireCode(VaultState state, Guid shareCodeId) =>
        state.ShareCodes.TryGetValue(shareCodeId, out var code)
            ? code
            : throw new NotFoundException($"Share code '{shareCodeId}' not found.");

    /// <summary>
    /// Reports a lapsed code as Expired even before the sweep has written the
    /// event, so the user never sees a code presented as live when it is not.
    /// </summary>
    private static ShareCodeStatus EffectiveStatus(ShareCode code, DateTimeOffset now) =>
        code.Status is ShareCodeStatus.Pending or ShareCodeStatus.AwaitingApproval && code.CodeExpiresAt <= now
            ? ShareCodeStatus.Expired
            : code.Status;

    /// <summary>
    /// Buckets selected fields by the category that owns them. Fields whose
    /// definition has since been deleted are dropped.
    /// </summary>
    private static IReadOnlyList<KeyValuePair<Guid, IReadOnlyList<Guid>>> GroupByCategory(
        VaultState state, IReadOnlyList<Guid> fieldIds)
    {
        var byCategory = new Dictionary<Guid, List<Guid>>();
        foreach (var fieldId in fieldIds)
        {
            if (!state.FieldDefinitions.TryGetValue(fieldId, out var field))
            {
                continue;
            }

            if (!byCategory.TryGetValue(field.CategoryId, out var bucket))
            {
                bucket = byCategory[field.CategoryId] = [];
            }

            bucket.Add(fieldId);
        }

        return byCategory
            .Select(kvp => new KeyValuePair<Guid, IReadOnlyList<Guid>>(kvp.Key, kvp.Value))
            .ToList();
    }

    private static IReadOnlyList<SharedFieldView> DescribeFields(VaultState state, IReadOnlyList<Guid> fieldIds)
    {
        var views = new List<SharedFieldView>();
        foreach (var fieldId in fieldIds)
        {
            if (!state.FieldDefinitions.TryGetValue(fieldId, out var field))
            {
                continue;
            }

            var categoryName = state.Categories.TryGetValue(field.CategoryId, out var category)
                ? category.Name
                : string.Empty;

            views.Add(new SharedFieldView(field.Id, field.CategoryId, categoryName, field.Name));
        }

        return views;
    }

    private async Task<string> SafeOrgNameAsync(string organisationId, CancellationToken ct)
    {
        try
        {
            return (await _organisations.GetAsync(organisationId, ct)).Name;
        }
        catch (NotFoundException)
        {
            return organisationId;
        }
    }
}

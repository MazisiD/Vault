using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VaultID.Api.Contracts;
using VaultID.Api.Infrastructure;
using VaultID.Application.Contracts;
using VaultID.Application.Services;
using VaultID.Domain;

namespace VaultID.Api.Controllers;

/// <summary>
/// User-facing sharing endpoints: list grants, share a category, revoke, renew,
/// reschedule, run the expiry sweep, and drive the share-code flow (generate a
/// code, review what an organisation redeemed, approve or reject it).
/// Transport only.
/// </summary>
[ApiController]
[Route("api/vaults/{userId}/shares")]
[Authorize]
[EnforceOwnVaultUser]
public sealed class SharingController(SharingService sharing, ShareCodeService shareCodes) : ControllerBase
{
    private readonly SharingService _sharing = sharing;
    private readonly ShareCodeService _shareCodes = shareCodes;

    [HttpGet("~/api/vaults/{userId}/grants")]
    public async Task<ActionResult<IReadOnlyList<GrantView>>> ListGrants(string userId, CancellationToken ct) =>
        Ok(await _sharing.ListGrantsAsync(userId, ct));

    [HttpPost]
    public async Task<ActionResult<GrantView>> Share(string userId, ShareBody body, CancellationToken ct)
    {
        var grant = await _sharing.ShareCategoryAsync(
            userId,
            new ShareRequest(body.OrganisationId, body.CategoryId, body.Scope, body.Duration, body.ConsentMethod),
            ct);
        return Ok(grant);
    }

    [HttpPost("{grantId:guid}/revoke")]
    public async Task<IActionResult> Revoke(string userId, Guid grantId, CancellationToken ct)
    {
        await _sharing.RevokeAsync(userId, grantId, ct);
        return NoContent();
    }

    [HttpPost("{grantId:guid}/renew")]
    public async Task<ActionResult<GrantView>> Renew(string userId, Guid grantId, RenewBody body, CancellationToken ct) =>
        Ok(await _sharing.RenewAsync(userId, new RenewShareRequest(grantId, body.NewDuration), ct));

    /// <summary>Moves an existing share's end date, forwards or backwards.</summary>
    [HttpPut("{grantId:guid}/expiry")]
    public async Task<ActionResult<GrantView>> ChangeExpiry(
        string userId, Guid grantId, ChangeExpiryBody body, CancellationToken ct) =>
        Ok(await _sharing.ChangeExpiryAsync(userId, new ChangeExpiryRequest(grantId, body.NewExpiresAt), ct));

    /// <summary>Runs the renewal/expiry sweep for a vault (would be a scheduled job).</summary>
    [HttpPost("process-expiries")]
    public async Task<IActionResult> ProcessExpiries(string userId, CancellationToken ct)
    {
        await _sharing.ProcessExpiriesAsync(userId, ct);
        return NoContent();
    }

    // --- Share codes ---

    [HttpGet("~/api/vaults/{userId}/share-codes")]
    public async Task<ActionResult<IReadOnlyList<ShareCodeView>>> ListShareCodes(string userId, CancellationToken ct) =>
        Ok(await _shareCodes.ListAsync(userId, ct));

    /// <summary>
    /// Mints a code. The response is the one and only time the plaintext code is
    /// exposed, so it is never cached downstream.
    /// </summary>
    [HttpPost("~/api/vaults/{userId}/share-codes")]
    public async Task<ActionResult<GeneratedShareCodeView>> GenerateShareCode(
        string userId, GenerateShareCodeBody body, CancellationToken ct)
    {
        var generated = await _shareCodes.GenerateAsync(
            userId,
            new GenerateShareCodeRequest(
                body.OrganisationId,
                body.FieldDefinitionIds,
                body.AccessExpiresAt,
                body.Scope ?? AccessScope.ReadOnly,
                body.ConsentMethod ?? ConsentMethod.InAppConfirmation),
            ct);

        Response.Headers.CacheControl = "no-store";
        return Ok(generated);
    }

    /// <summary>Cancels a code the user no longer wants honoured.</summary>
    [HttpPost("~/api/vaults/{userId}/share-codes/{shareCodeId:guid}/revoke")]
    public async Task<IActionResult> RevokeShareCode(string userId, Guid shareCodeId, CancellationToken ct)
    {
        await _shareCodes.RevokeAsync(userId, shareCodeId, ct);
        return NoContent();
    }

    /// <summary>Requests raised by organisations that redeemed a code, awaiting the user's decision.</summary>
    [HttpGet("~/api/vaults/{userId}/share-requests")]
    public async Task<ActionResult<IReadOnlyList<PendingShareRequestView>>> ListPendingRequests(
        string userId, CancellationToken ct) =>
        Ok(await _shareCodes.ListPendingRequestsAsync(userId, ct));

    [HttpPost("~/api/vaults/{userId}/share-requests/{shareCodeId:guid}/approve")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> ApproveRequest(
        string userId, Guid shareCodeId, ApproveShareCodeBody body, CancellationToken ct) =>
        Ok(await _shareCodes.ApproveAsync(
            userId, shareCodeId, body.ConsentMethod ?? ConsentMethod.InAppConfirmation, ct));

    [HttpPost("~/api/vaults/{userId}/share-requests/{shareCodeId:guid}/reject")]
    public async Task<IActionResult> RejectRequest(string userId, Guid shareCodeId, CancellationToken ct)
    {
        await _shareCodes.RejectAsync(userId, shareCodeId, ct);
        return NoContent();
    }
}

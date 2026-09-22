using Microsoft.AspNetCore.Mvc;
using VaultID.Api.Contracts;
using VaultID.Application.Contracts;
using VaultID.Application.Services;

namespace VaultID.Api.Controllers;

/// <summary>
/// The public, organisation-facing REST API (blueprint 4.7). This is the
/// "reference, not copy" surface: organisations query live data and never
/// receive a stored copy. Every call is permission-checked and audit-logged by
/// the Application layer.
/// <para>
/// Organisation identity is taken from the <c>X-Org-Id</c> header for this demo.
/// In production this is the subject of an OAuth 2.0 client-credentials access
/// token, scoped to the categories the user granted (blueprint 4.7 / 10).
/// </para>
/// </summary>
[ApiController]
[Route("v1")]
public sealed class OrganisationApiController(
    OrganisationDataAccessService dataAccess,
    WebhookNotificationService webhooks,
    GrantQueryService grants,
    ShareCodeService shareCodes) : ControllerBase
{
    private readonly OrganisationDataAccessService _dataAccess = dataAccess;
    private readonly WebhookNotificationService _webhooks = webhooks;
    private readonly GrantQueryService _grants = grants;
    private readonly ShareCodeService _shareCodes = shareCodes;

    /// <summary>
    /// Redeems a share code the user handed over out-of-band. This grants no
    /// access on its own: it raises a request the user must approve after
    /// reading this organisation's data-processing agreement. Poll
    /// <c>GET /v1/grants</c> to see when the resulting grants go live.
    /// </summary>
    [HttpPost("share-codes/redeem")]
    public async Task<IActionResult> RedeemShareCode(RedeemShareCodeBody body, CancellationToken ct)
    {
        if (!TryGetOrg(out var orgId))
        {
            return MissingOrg();
        }

        return Ok(await _shareCodes.RedeemAsync(orgId, body.Code, Ip(), ct));
    }

    [HttpGet("vault/{user}/categories/{categoryId:guid}")]
    public async Task<IActionResult> GetCategory(string user, Guid categoryId, CancellationToken ct)
    {
        if (!TryGetOrg(out var orgId))
        {
            return MissingOrg();
        }

        var result = await _dataAccess.QueryCategoryAsync(user, orgId, categoryId, Ip(), ct);
        return result.Allowed
            ? Ok(new { user, result.CategoryId, result.Fields, result.FieldNames })
            : Forbid403(result.DenialReason);
    }

    [HttpGet("vault/{user}/fields/{fieldId:guid}")]
    public async Task<IActionResult> GetField(string user, Guid fieldId, CancellationToken ct)
    {
        if (!TryGetOrg(out var orgId))
        {
            return MissingOrg();
        }

        var result = await _dataAccess.QueryFieldAsync(user, orgId, fieldId, Ip(), ct);
        return result.Allowed
            ? Ok(new { user, fieldId, value = result.Fields.GetValueOrDefault(fieldId) })
            : Forbid403(result.DenialReason);
    }

    [HttpPost("vault/{user}/verify/{fieldId:guid}")]
    public async Task<IActionResult> Verify(string user, Guid fieldId, VerifyBody body, CancellationToken ct)
    {
        if (!TryGetOrg(out var orgId))
        {
            return MissingOrg();
        }

        var (allowed, reason, matches) = await _dataAccess.VerifyFieldAsync(user, orgId, fieldId, body.Value, Ip(), ct);
        return allowed
            ? Ok(new VerifyResult(matches))
            : Forbid403(reason);
    }

    [HttpPost("webhooks")]
    public async Task<ActionResult<WebhookSubscriptionView>> Subscribe(SubscribeWebhookBody body, CancellationToken ct)
    {
        if (!TryGetOrg(out var orgId))
        {
            return MissingOrg();
        }

        var sub = await _webhooks.SubscribeAsync(orgId, body.UserId, body.CallbackUrl, ct);
        return Ok(sub);
    }

    [HttpDelete("webhooks/{id}")]
    public async Task<IActionResult> Unsubscribe(string id, CancellationToken ct)
    {
        if (!TryGetOrg(out _))
        {
            return MissingOrg();
        }

        await _webhooks.UnsubscribeAsync(id, ct);
        return NoContent();
    }

    [HttpGet("grants")]
    public async Task<IActionResult> ListGrants(CancellationToken ct)
    {
        if (!TryGetOrg(out var orgId))
        {
            return MissingOrg();
        }

        return Ok(await _grants.ListActiveForOrganisationAsync(orgId, ct));
    }

    /// <summary>
    /// Every share-code request this organisation has redeemed, whatever its
    /// current status - awaiting approval, approved, rejected, revoked or expired.
    /// </summary>
    [HttpGet("share-requests")]
    public async Task<IActionResult> ListShareRequests(CancellationToken ct)
    {
        if (!TryGetOrg(out var orgId))
        {
            return MissingOrg();
        }

        return Ok(await _grants.ListShareRequestsForOrganisationAsync(orgId, ct));
    }

    // --- Helpers (transport concerns only) ---

    private bool TryGetOrg(out string organisationId)
    {
        organisationId = Request.Headers["X-Org-Id"].ToString();
        return !string.IsNullOrWhiteSpace(organisationId);
    }

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private ObjectResult MissingOrg() =>
        StatusCode(StatusCodes.Status401Unauthorized, new { title = "Missing organisation credentials", detail = "Provide the X-Org-Id header (OAuth2 token subject in production)." });

    private ObjectResult Forbid403(string? reason) =>
        StatusCode(StatusCodes.Status403Forbidden, new { title = "Access denied", detail = reason ?? "Not permitted." });
}

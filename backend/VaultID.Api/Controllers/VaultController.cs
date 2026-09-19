using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VaultID.Api.Contracts;
using VaultID.Api.Infrastructure;
using VaultID.Application.Contracts;
using VaultID.Application.Services;

namespace VaultID.Api.Controllers;

/// <summary>
/// User-facing vault endpoints (create vault, view/update fields, activity
/// feed). Pure transport: each action delegates to an Application service.
/// </summary>
[ApiController]
[Route("api/vaults")]
[Authorize]
[EnforceOwnVaultUser]
public sealed class VaultController(VaultService vaults, ActivityService activity) : ControllerBase
{
    private readonly VaultService _vaults = vaults;
    private readonly ActivityService _activity = activity;

    [HttpPost]
    public async Task<ActionResult<VaultSummary>> Create(CreateVaultBody body, CancellationToken ct)
    {
        // The route carries no {userId} here for the filter to check, so the
        // vault's owner is taken from the authenticated identity directly -
        // never trust the client-supplied body.UserId for who owns it.
        var userId = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Authenticated request is missing a 'sub' claim.");
        var summary = await _vaults.CreateVaultAsync(new CreateVaultRequest(userId, body.DisplayName), ct);
        return CreatedAtAction(nameof(GetSummary), new { userId = summary.UserId }, summary);
    }

    [HttpGet("{userId}")]
    public async Task<ActionResult<VaultSummary>> GetSummary(string userId, CancellationToken ct) =>
        Ok(await _vaults.GetSummaryAsync(userId, ct));

    [HttpGet("{userId}/categories/{categoryId:guid}")]
    public async Task<ActionResult<CategoryView>> GetCategory(string userId, Guid categoryId, CancellationToken ct) =>
        Ok(await _vaults.GetCategoryAsync(userId, categoryId, ct));

    [HttpPut("{userId}/fields/{fieldDefinitionId:guid}")]
    public async Task<IActionResult> UpdateField(string userId, Guid fieldDefinitionId, UpdateFieldBody body, CancellationToken ct)
    {
        await _vaults.UpdateFieldAsync(userId, new UpdateFieldRequest(fieldDefinitionId, body.Value), ct);
        return NoContent();
    }

    /// <summary>
    /// Saves a whole category's edits at once. Either every value is accepted or
    /// none is, and the response carries the category's values as they now
    /// stand so the client never has to guess what landed.
    /// </summary>
    [HttpPut("{userId}/categories/{categoryId:guid}/fields")]
    public async Task<ActionResult<CategoryView>> UpdateCategoryFields(
        string userId, Guid categoryId, UpdateCategoryFieldsBody body, CancellationToken ct)
    {
        var values = body.Values
            .Select(v => new FieldValueUpdate(v.FieldDefinitionId, v.Value))
            .ToList();

        var collections = (body.Collections ?? [])
            .Select(c => new CollectionUpdate(
                c.FieldDefinitionId,
                c.Items
                    .Select(i => new CollectionItemUpdate(
                        i.ItemId,
                        i.Values.Select(v => new FieldValueUpdate(v.FieldDefinitionId, v.Value)).ToList()))
                    .ToList()))
            .ToList();

        var request = new UpdateCategoryFieldsRequest(categoryId, values, collections);
        return Ok(await _vaults.UpdateCategoryFieldsAsync(userId, request, ct));
    }

    [HttpGet("{userId}/activity")]
    public async Task<ActionResult<IReadOnlyList<ActivityEntry>>> GetActivity(string userId, CancellationToken ct) =>
        Ok(await _activity.GetFeedAsync(userId, ct));
}

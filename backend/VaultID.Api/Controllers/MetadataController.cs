using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VaultID.Api.Infrastructure;
using VaultID.Application.Contracts;
using VaultID.Application.Services;

namespace VaultID.Api.Controllers;

/// <summary>
/// Reference-data endpoint (the category/field schema) used by the UI to
/// render forms. Transport only.
/// <para>
/// Since the dynamic-categories spec the schema is per-vault (a user can add
/// their own categories/fields on top of the 3 seeded system ones), so this
/// is nested under the owning vault like the other user-facing endpoints.
/// </para>
/// </summary>
[ApiController]
[Route("api/vaults/{userId}/metadata")]
[Authorize]
[EnforceOwnVaultUser]
public sealed class MetadataController(CategoryMetadataService metadata) : ControllerBase
{
    private readonly CategoryMetadataService _metadata = metadata;

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategorySchemaView>>> GetCategories(string userId, CancellationToken ct) =>
        Ok(await _metadata.GetCategoriesAsync(userId, ct));
}

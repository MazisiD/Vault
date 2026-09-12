using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VaultID.Api.Contracts;
using VaultID.Api.Infrastructure;
using VaultID.Application.Contracts;
using VaultID.Application.Services;

namespace VaultID.Api.Controllers;

/// <summary>
/// Category and field-definition schema management endpoints
/// (dynamic-categories spec): create/rename/delete a category, and
/// create/rename/delete a field (or Group) within it. These are vault-owner
/// operations with no sharing implications of their own. Transport only - all
/// guardrails (system-category, non-empty, has-value, group-cascade) are
/// enforced by <see cref="CategorySchemaService"/>.
/// </summary>
[ApiController]
[Route("api/vaults/{userId}/categories")]
[Authorize]
[EnforceOwnVaultUser]
public sealed class CategoriesController(CategorySchemaService schema) : ControllerBase
{
    private readonly CategorySchemaService _schema = schema;

    [HttpPost]
    public async Task<ActionResult<CategorySchemaView>> CreateCategory(string userId, CreateCategoryBody body, CancellationToken ct)
    {
        var category = await _schema.CreateCategoryAsync(userId, new CreateCategoryRequest(body.Name), ct);
        return Ok(category);
    }

    [HttpPut("{categoryId:guid}")]
    public async Task<IActionResult> RenameCategory(string userId, Guid categoryId, RenameCategoryBody body, CancellationToken ct)
    {
        await _schema.RenameCategoryAsync(userId, categoryId, body.Name, ct);
        return NoContent();
    }

    [HttpDelete("{categoryId:guid}")]
    public async Task<IActionResult> DeleteCategory(string userId, Guid categoryId, CancellationToken ct)
    {
        await _schema.DeleteCategoryAsync(userId, categoryId, ct);
        return NoContent();
    }

    [HttpPost("{categoryId:guid}/fields")]
    public async Task<ActionResult<FieldDefinitionView>> CreateField(
        string userId, Guid categoryId, CreateFieldBody body, CancellationToken ct)
    {
        var field = await _schema.CreateFieldAsync(
            userId, categoryId,
            new CreateFieldRequest(body.Name, body.FieldType, body.AutocompleteToken, body.Choices, body.ParentFieldDefinitionId),
            ct);
        return Ok(field);
    }

    [HttpPut("{categoryId:guid}/fields/{fieldId:guid}")]
    public async Task<IActionResult> RenameField(
        string userId, Guid categoryId, Guid fieldId, RenameFieldBody body, CancellationToken ct)
    {
        await _schema.RenameFieldAsync(userId, categoryId, fieldId, body.Name, ct);
        return NoContent();
    }

    [HttpDelete("{categoryId:guid}/fields/{fieldId:guid}")]
    public async Task<IActionResult> DeleteField(string userId, Guid categoryId, Guid fieldId, CancellationToken ct)
    {
        await _schema.DeleteFieldAsync(userId, categoryId, fieldId, ct);
        return NoContent();
    }
}

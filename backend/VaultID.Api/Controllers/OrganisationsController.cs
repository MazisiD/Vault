using Microsoft.AspNetCore.Mvc;
using VaultID.Api.Contracts;
using VaultID.Application.Contracts;
using VaultID.Application.Services;
using VaultID.Domain.Categories;

namespace VaultID.Api.Controllers;

/// <summary>
/// Organisation registry endpoints: register an org with its DPA, search the
/// verified registry, and fetch the agreement a user reviews before sharing
/// (blueprint 5.8). Transport only.
/// </summary>
[ApiController]
[Route("api/organisations")]
public sealed class OrganisationsController(OrganisationRegistryService registry) : ControllerBase
{
    private readonly OrganisationRegistryService _registry = registry;

    [HttpPost]
    public async Task<ActionResult<OrganisationView>> Register(RegisterOrganisationBody body, CancellationToken ct)
    {
        var org = await _registry.RegisterAsync(new RegisterOrganisationRequest(
            body.Name, body.Purpose, body.RetentionDays, body.LegalBasis,
            body.ThirdPartySharing, body.DeletionCommitment,
            body.RegistrationNumber, body.Address, body.Industry,
            body.ContactName, body.ContactPhone, body.ContactEmail), ct);
        return CreatedAtAction(nameof(Get), new { id = org.Id }, org);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganisationView>>> Search([FromQuery] string? query, CancellationToken ct) =>
        Ok(await _registry.SearchAsync(query, ct));

    [HttpGet("{id}")]
    public async Task<ActionResult<OrganisationView>> Get(string id, CancellationToken ct) =>
        Ok(await _registry.GetAsync(id, ct));

    [HttpGet("{id}/agreement")]
    public async Task<ActionResult<AgreementView>> GetAgreement(string id, CancellationToken ct) =>
        Ok(await _registry.GetAgreementAsync(id, ct));

    /// <summary>The fixed set of system category names every vault seeds, for the org's per-category agreement step.</summary>
    [HttpGet("category-catalog")]
    public ActionResult<IReadOnlyList<string>> GetCategoryCatalog() =>
        Ok(CategoryCatalog.SystemCategories.Select(c => c.Name).ToList());

    [HttpPut("{id}/profile")]
    public async Task<ActionResult<OrganisationView>> UpdateProfile(string id, UpdateOrganisationProfileBody body, CancellationToken ct) =>
        Ok(await _registry.UpdateProfileAsync(id, new UpdateOrganisationProfileRequest(
            body.Name, body.RegistrationNumber, body.Address, body.Industry,
            body.ContactName, body.ContactPhone, body.ContactEmail), ct));

    [HttpPut("{id}/compliance")]
    public async Task<ActionResult<OrganisationView>> UpdateCompliance(string id, UpdateOrganisationComplianceBody body, CancellationToken ct) =>
        Ok(await _registry.UpdateComplianceAsync(id, new UpdateOrganisationComplianceRequest(
            body.Purpose, body.RetentionDays, body.LegalBasis, body.ThirdPartySharing, body.DeletionCommitment), ct));

    [HttpGet("{id}/agreements")]
    public async Task<ActionResult<IReadOnlyList<CategoryAgreementView>>> ListCategoryAgreements(string id, CancellationToken ct) =>
        Ok(await _registry.ListCategoryAgreementsAsync(id, ct));

    [HttpPut("{id}/agreements")]
    public async Task<ActionResult<CategoryAgreementView>> SetCategoryAgreement(string id, SetCategoryAgreementBody body, CancellationToken ct) =>
        Ok(await _registry.SetCategoryAgreementAsync(id, new SetCategoryAgreementRequest(
            body.CategoryName, body.Purpose, body.RetentionDays, body.LegalBasis,
            body.ThirdPartySharing, body.DeletionCommitment), ct));

    [HttpDelete("{id}/agreements/{categoryName}")]
    public async Task<ActionResult<CategoryAgreementView>> ClearCategoryAgreement(string id, string categoryName, CancellationToken ct) =>
        Ok(await _registry.ClearCategoryAgreementAsync(id, categoryName, ct));

    [HttpPost("{id}/invites")]
    public async Task<ActionResult<IReadOnlyList<string>>> AddPendingInvites(string id, AddPendingInvitesBody body, CancellationToken ct) =>
        Ok(await _registry.AddPendingInvitesAsync(id, new AddPendingInvitesRequest(body.Emails), ct));
}

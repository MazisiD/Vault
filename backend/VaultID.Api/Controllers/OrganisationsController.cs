using Microsoft.AspNetCore.Mvc;
using VaultID.Api.Contracts;
using VaultID.Application.Contracts;
using VaultID.Application.Services;

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
            body.ThirdPartySharing, body.DeletionCommitment), ct);
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
}

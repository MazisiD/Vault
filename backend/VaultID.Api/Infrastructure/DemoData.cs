using VaultID.Application.Contracts;
using VaultID.Application.Services;

namespace VaultID.Api.Infrastructure;

/// <summary>
/// Seeds a few approved organisations so the in-memory platform is usable
/// out-of-the-box for demos. This is dev convenience only.
/// </summary>
public static class DemoData
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<OrganisationRegistryService>();

        var existing = await registry.SearchAsync(null);
        if (existing.Count > 0)
        {
            return;
        }

        await registry.RegisterAsync(new RegisterOrganisationRequest(
            Name: "FNB Bank",
            Purpose: "To process your home loan application and verify your identity.",
            RetentionDays: 365,
            LegalBasis: "Contractual obligation",
            ThirdPartySharing: "Credit bureaus for affordability checks.",
            DeletionCommitment: "Cached snapshots are deleted within 30 days of the sharing period ending."));

        await registry.RegisterAsync(new RegisterOrganisationRequest(
            Name: "University of Cape Town",
            Purpose: "To issue and verify your academic records.",
            RetentionDays: 1825,
            LegalBasis: "Legal requirement (record-keeping)",
            ThirdPartySharing: "None.",
            DeletionCommitment: "No data is cached; all access is live via the API."));

        await registry.RegisterAsync(new RegisterOrganisationRequest(
            Name: "Discovery Health",
            Purpose: "To administer your medical aid membership and claims.",
            RetentionDays: 730,
            LegalBasis: "Contractual obligation",
            ThirdPartySharing: "Contracted healthcare providers for claim processing.",
            DeletionCommitment: "Cached data deleted within 60 days of membership ending."));
    }
}

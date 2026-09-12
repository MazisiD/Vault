using System.Security.Cryptography;
using System.Text;
using VaultID.Application.Common;
using VaultID.Application.Contracts;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Application.Services;

/// <summary>
/// Organisation registry and data processing agreement (DPA) management
/// (blueprint 5.8). An organisation registers, submits DPA terms, and the
/// platform reviews them for POPIA compliance before the org goes live. Users
/// always see the org's agreement before sharing.
/// </summary>
public sealed class OrganisationRegistryService(IOrganisationStore store)
{
    private readonly IOrganisationStore _store = store;

    /// <summary>
    /// Registers an organisation with its DPA. Performs POPIA-oriented
    /// validation of the retention terms (blueprint 4.5 / POPIA s14).
    /// </summary>
    public async Task<OrganisationView> RegisterAsync(RegisterOrganisationRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LegalBasis);

        if (request.RetentionDays <= 0)
        {
            throw new ValidationException("Retention period must be a positive number of days (POPIA s14).");
        }

        // POPIA s14: data may only be kept as long as needed. We cap declared
        // cached-retention at a generous ceiling and flag anything beyond as
        // needing manual compliance review.
        const int complianceCeilingDays = 365 * 7;
        var status = request.RetentionDays > complianceCeilingDays ? "needs-review" : "approved";

        var record = new OrganisationRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.Name,
            Status = status,
            AgreementId = Guid.NewGuid().ToString("N"),
            AgreementPurpose = request.Purpose,
            AgreementRetentionDays = request.RetentionDays,
            AgreementLegalBasis = request.LegalBasis,
            AgreementThirdPartySharing = request.ThirdPartySharing,
            AgreementDeletionCommitment = request.DeletionCommitment
        };

        await _store.UpsertAsync(record, ct);
        return ToView(record);
    }

    public async Task<OrganisationView> GetAsync(string organisationId, CancellationToken ct = default)
    {
        var record = await _store.GetByIdAsync(organisationId, ct)
            ?? throw new NotFoundException($"Organisation '{organisationId}' not found.");
        return ToView(record);
    }

    public async Task<IReadOnlyList<OrganisationView>> SearchAsync(string? query, CancellationToken ct = default)
    {
        var records = await _store.SearchAsync(query, ct);
        return records.Select(ToView).ToList();
    }

    /// <summary>Returns the agreement an org would present to a user before sharing.</summary>
    public async Task<AgreementView> GetAgreementAsync(string organisationId, CancellationToken ct = default)
    {
        var record = await _store.GetByIdAsync(organisationId, ct)
            ?? throw new NotFoundException($"Organisation '{organisationId}' not found.");

        if (record.AgreementId is null)
        {
            throw new ConflictException("This organisation has not submitted a data processing agreement.");
        }

        return ToAgreement(record)!;
    }

    /// <summary>Stable hash of the agreement text the user is consenting to.</summary>
    public static string ComputeAgreementHash(AgreementView agreement)
    {
        var canonical = string.Join('|',
            agreement.AgreementId,
            agreement.Purpose,
            agreement.RetentionDays,
            agreement.LegalBasis,
            agreement.ThirdPartySharing,
            agreement.DeletionCommitment);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static OrganisationView ToView(OrganisationRecord r) =>
        new(r.Id, r.Name, r.Status, ToAgreement(r));

    private static AgreementView? ToAgreement(OrganisationRecord r) =>
        r.AgreementId is null
            ? null
            : new AgreementView(
                r.Id,
                r.Name,
                r.AgreementId,
                r.AgreementPurpose ?? string.Empty,
                r.AgreementRetentionDays ?? 0,
                r.AgreementLegalBasis ?? string.Empty,
                r.AgreementThirdPartySharing,
                r.AgreementDeletionCommitment);
}

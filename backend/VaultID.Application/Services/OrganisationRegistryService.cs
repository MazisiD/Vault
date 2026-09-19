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

        ValidateRetention(request.RetentionDays);

        var record = new OrganisationRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.Name,
            Status = StatusFor(request.RetentionDays),
            AgreementId = Guid.NewGuid().ToString("N"),
            AgreementPurpose = request.Purpose,
            AgreementRetentionDays = request.RetentionDays,
            AgreementLegalBasis = request.LegalBasis,
            AgreementThirdPartySharing = request.ThirdPartySharing,
            AgreementDeletionCommitment = request.DeletionCommitment,
            RegistrationNumber = request.RegistrationNumber,
            Address = request.Address,
            Industry = request.Industry,
            ContactName = request.ContactName,
            ContactPhone = request.ContactPhone,
            ContactEmail = request.ContactEmail
        };

        await _store.UpsertAsync(record, ct);
        return ToView(record);
    }

    public async Task<OrganisationView> GetAsync(string organisationId, CancellationToken ct = default)
    {
        var record = await RequireAsync(organisationId, ct);
        return ToView(record);
    }

    public async Task<IReadOnlyList<OrganisationView>> SearchAsync(string? query, CancellationToken ct = default)
    {
        var records = await _store.SearchAsync(query, ct);
        return records.Select(ToView).ToList();
    }

    /// <summary>Updates the organisation's profile fields (name and contact/registration details).</summary>
    public async Task<OrganisationView> UpdateProfileAsync(
        string organisationId, UpdateOrganisationProfileRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var record = await RequireAsync(organisationId, ct);
        record.Name = request.Name;
        record.RegistrationNumber = request.RegistrationNumber;
        record.Address = request.Address;
        record.Industry = request.Industry;
        record.ContactName = request.ContactName;
        record.ContactPhone = request.ContactPhone;
        record.ContactEmail = request.ContactEmail;

        await _store.UpsertAsync(record, ct);
        return ToView(record);
    }

    /// <summary>Updates the organisation-wide default agreement terms (blueprint 5.8, POPIA s14).</summary>
    public async Task<OrganisationView> UpdateComplianceAsync(
        string organisationId, UpdateOrganisationComplianceRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LegalBasis);
        ValidateRetention(request.RetentionDays);

        var record = await RequireAsync(organisationId, ct);
        record.Status = StatusFor(request.RetentionDays);
        record.AgreementPurpose = request.Purpose;
        record.AgreementRetentionDays = request.RetentionDays;
        record.AgreementLegalBasis = request.LegalBasis;
        record.AgreementThirdPartySharing = request.ThirdPartySharing;
        record.AgreementDeletionCommitment = request.DeletionCommitment;

        await _store.UpsertAsync(record, ct);
        return ToView(record);
    }

    /// <summary>Sets or replaces one category's own agreement terms, overriding the org default.</summary>
    public async Task<CategoryAgreementView> SetCategoryAgreementAsync(
        string organisationId, SetCategoryAgreementRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CategoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LegalBasis);
        ValidateRetention(request.RetentionDays);

        var record = await RequireAsync(organisationId, ct);
        record.CategoryAgreements[request.CategoryName] = new CategoryAgreementRecord
        {
            Purpose = request.Purpose,
            RetentionDays = request.RetentionDays,
            LegalBasis = request.LegalBasis,
            ThirdPartySharing = request.ThirdPartySharing,
            DeletionCommitment = request.DeletionCommitment
        };

        await _store.UpsertAsync(record, ct);
        return ToCategoryAgreement(record, request.CategoryName);
    }

    /// <summary>Removes a category's override, reverting it to the organisation's default agreement.</summary>
    public async Task<CategoryAgreementView> ClearCategoryAgreementAsync(
        string organisationId, string categoryName, CancellationToken ct = default)
    {
        var record = await RequireAsync(organisationId, ct);
        record.CategoryAgreements.Remove(categoryName);
        await _store.UpsertAsync(record, ct);
        return ToCategoryAgreement(record, categoryName);
    }

    /// <summary>Every category the org has customised, plus its effective agreement.</summary>
    public async Task<IReadOnlyList<CategoryAgreementView>> ListCategoryAgreementsAsync(
        string organisationId, CancellationToken ct = default)
    {
        var record = await RequireAsync(organisationId, ct);
        return record.CategoryAgreements.Keys
            .Select(name => ToCategoryAgreement(record, name))
            .ToList();
    }

    /// <summary>Adds emails to the organisation's pending (not yet linked) admin/operator invite list.</summary>
    public async Task<IReadOnlyList<string>> AddPendingInvitesAsync(
        string organisationId, AddPendingInvitesRequest request, CancellationToken ct = default)
    {
        var record = await RequireAsync(organisationId, ct);
        foreach (var email in request.Emails)
        {
            if (!string.IsNullOrWhiteSpace(email) &&
                !record.PendingInvites.Contains(email, StringComparer.OrdinalIgnoreCase))
            {
                record.PendingInvites.Add(email);
            }
        }

        await _store.UpsertAsync(record, ct);
        return record.PendingInvites;
    }

    /// <summary>Returns the agreement an org would present to a user before sharing (the org-wide default).</summary>
    public async Task<AgreementView> GetAgreementAsync(string organisationId, CancellationToken ct = default)
    {
        var record = await RequireAsync(organisationId, ct);
        return ToAgreement(record) ?? throw new ConflictException(
            "This organisation has not submitted a data processing agreement.");
    }

    /// <summary>
    /// The agreement to present/sign for one category: that category's own
    /// override if the org has set one, otherwise the organisation-wide default.
    /// </summary>
    public async Task<AgreementView> GetAgreementForCategoryAsync(
        string organisationId, string categoryName, CancellationToken ct = default)
    {
        var record = await RequireAsync(organisationId, ct);
        if (record.CategoryAgreements.TryGetValue(categoryName, out var overrideTerms))
        {
            return ToAgreement(record.Id, record.Name, $"{record.Id}:{categoryName}".ToLowerInvariant(), overrideTerms);
        }

        return ToAgreement(record) ?? throw new ConflictException(
            $"Organisation '{record.Name}' has not set up a data processing agreement for '{categoryName}' or a default one.");
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

    private async Task<OrganisationRecord> RequireAsync(string organisationId, CancellationToken ct) =>
        await _store.GetByIdAsync(organisationId, ct)
            ?? throw new NotFoundException($"Organisation '{organisationId}' not found.");

    private static void ValidateRetention(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            throw new ValidationException("Retention period must be a positive number of days (POPIA s14).");
        }
    }

    // POPIA s14: data may only be kept as long as needed. We cap declared
    // cached-retention at a generous ceiling and flag anything beyond as
    // needing manual compliance review.
    private const int ComplianceCeilingDays = 365 * 7;

    private static string StatusFor(int retentionDays) =>
        retentionDays > ComplianceCeilingDays ? "needs-review" : "approved";

    private static OrganisationView ToView(OrganisationRecord r) =>
        new(r.Id, r.Name, r.Status, ToAgreement(r), ToProfile(r), r.PendingInvites);

    private static OrganisationProfileView ToProfile(OrganisationRecord r) =>
        new(r.RegistrationNumber, r.Address, r.Industry, r.ContactName, r.ContactPhone, r.ContactEmail);

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

    private static AgreementView ToAgreement(
        string organisationId, string organisationName, string agreementId, CategoryAgreementRecord terms) =>
        new(
            organisationId,
            organisationName,
            agreementId,
            terms.Purpose,
            terms.RetentionDays,
            terms.LegalBasis,
            terms.ThirdPartySharing,
            terms.DeletionCommitment);

    private static CategoryAgreementView ToCategoryAgreement(OrganisationRecord r, string categoryName)
    {
        if (r.CategoryAgreements.TryGetValue(categoryName, out var overrideTerms))
        {
            return new CategoryAgreementView(
                categoryName, true, ToAgreement(r.Id, r.Name, $"{r.Id}:{categoryName}".ToLowerInvariant(), overrideTerms));
        }

        var fallback = ToAgreement(r) ?? throw new ConflictException(
            $"Organisation '{r.Name}' has not set up a data processing agreement for '{categoryName}' or a default one.");
        return new CategoryAgreementView(categoryName, false, fallback);
    }
}

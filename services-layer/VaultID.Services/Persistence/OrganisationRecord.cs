namespace VaultID.Services.Persistence;

/// <summary>
/// Persistence record for an organisation in the verified registry, including
/// its currently approved data processing agreement (DPA) terms.
/// <para>
/// Organisations are reference data (not part of a user's event-sourced vault
/// stream), so they are stored in their own table/collection. This record is a
/// plain data carrier - no behaviour, no rules.
/// </para>
/// </summary>
public sealed class OrganisationRecord
{
    public required string Id { get; init; }
    public required string Name { get; set; }

    /// <summary>Registry approval state. Interpreted by the backend.</summary>
    public string Status { get; set; } = "pending";

    // --- Current data processing agreement (DPA) terms (organisation-wide default) ---
    public string? AgreementId { get; set; }
    public string? AgreementPurpose { get; set; }
    public int? AgreementRetentionDays { get; set; }
    public string? AgreementLegalBasis { get; set; }
    public string? AgreementThirdPartySharing { get; set; }
    public string? AgreementDeletionCommitment { get; set; }

    // --- Organisation profile (collected during onboarding, editable by an admin later) ---
    public string? RegistrationNumber { get; set; }
    public string? Address { get; set; }
    public string? Industry { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Per-category agreement overrides, keyed by category name (case-insensitive) -
    /// categories are per-vault entities without a shared id, so the name is the
    /// only stable key an org-level record can use. A category without an entry
    /// here falls back to the organisation-wide default agreement above.
    /// </summary>
    public Dictionary<string, CategoryAgreementRecord> CategoryAgreements { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Emails invited as additional org admins/operators, not yet linked to an account.</summary>
    public List<string> PendingInvites { get; set; } = [];

    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One category's own agreement terms, overriding the organisation's default.</summary>
public sealed class CategoryAgreementRecord
{
    public required string Purpose { get; set; }
    public required int RetentionDays { get; set; }
    public required string LegalBasis { get; set; }
    public string? ThirdPartySharing { get; set; }
    public string? DeletionCommitment { get; set; }
}

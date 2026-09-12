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

    // --- Current data processing agreement (DPA) terms ---
    public string? AgreementId { get; set; }
    public string? AgreementPurpose { get; set; }
    public int? AgreementRetentionDays { get; set; }
    public string? AgreementLegalBasis { get; set; }
    public string? AgreementThirdPartySharing { get; set; }
    public string? AgreementDeletionCommitment { get; set; }

    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

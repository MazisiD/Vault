namespace VaultID.Domain.Models;

/// <summary>
/// A permission grant as defined in the blueprint (4.4). This is reconstructed
/// state (projected from the event stream), representing the relationship that
/// lets one organisation reference one category of a user's vault.
/// </summary>
public sealed class PermissionGrant
{
    public required Guid Id { get; init; }

    /// <summary>The user who owns the vault.</summary>
    public required string GrantorUserId { get; init; }

    /// <summary>The organisation receiving access.</summary>
    public required string GranteeOrganisationId { get; init; }

    public required Guid CategoryId { get; init; }
    public required AccessScope Scope { get; init; }
    public required ShareDuration Duration { get; init; }

    /// <summary>
    /// The specific field definitions within the category this grant covers,
    /// as ticked by the user in the share-code flow. <c>null</c> means the whole
    /// category is shared - the behaviour of every grant created before
    /// field-level selection existed.
    /// </summary>
    public IReadOnlyList<Guid>? FieldDefinitionIds { get; init; }

    /// <summary>Reference to the signed data processing agreement.</summary>
    public required string AgreementId { get; init; }

    /// <summary>When the grant expires. Null means indefinite.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public GrantStatus Status { get; set; } = GrantStatus.Active;

    public DateTimeOffset ConsentedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// True when the grant currently permits access: it is active and, if it
    /// has an expiry, that expiry is in the future. Pure state inspection - the
    /// authoritative allow/deny decision is made by the Application permission
    /// engine, which also records audit events.
    /// </summary>
    public bool IsCurrentlyActive(DateTimeOffset now) =>
        Status == GrantStatus.Active && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>
    /// True when this grant exposes the given field. A grant with no field
    /// restriction covers every field in its category; a field-restricted grant
    /// covers only the exact definitions the user ticked.
    /// </summary>
    public bool CoversField(Guid fieldDefinitionId) =>
        FieldDefinitionIds is null || FieldDefinitionIds.Contains(fieldDefinitionId);
}

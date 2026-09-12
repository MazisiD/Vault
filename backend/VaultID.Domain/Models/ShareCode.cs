namespace VaultID.Domain.Models;

/// <summary>
/// A share code: the credential a user generates, hands to an organisation
/// out-of-band, and which that organisation redeems to raise an access request.
/// This is reconstructed state, projected from the vault's event stream.
/// <para>
/// Redemption alone grants nothing. It moves the code to
/// <see cref="ShareCodeStatus.AwaitingApproval"/> and surfaces the request to
/// the user, who reviews the organisation's data-processing agreement and then
/// approves or rejects. Only approval creates permission grants.
/// </para>
/// <para>
/// The plaintext code is never stored - only <see cref="CodeHash"/>.
/// </para>
/// </summary>
public sealed class ShareCode
{
    public required Guid Id { get; init; }

    /// <summary>Hex-encoded SHA-256 of the normalised code.</summary>
    public required string CodeHash { get; init; }

    /// <summary>The only organisation permitted to redeem this code.</summary>
    public required string OrganisationId { get; init; }

    /// <summary>
    /// The exact field definitions the user ticked. Grouped by owning category
    /// when the grants are created on approval.
    /// </summary>
    public required IReadOnlyList<Guid> FieldDefinitionIds { get; init; }

    /// <summary>
    /// The access expiry the user chose when generating the code. Applied to
    /// the resulting grants on approval, and changeable afterwards.
    /// </summary>
    public required DateTimeOffset AccessExpiresAt { get; init; }

    /// <summary>When the code itself stops being redeemable.</summary>
    public required DateTimeOffset CodeExpiresAt { get; init; }

    public ShareCodeStatus Status { get; set; } = ShareCodeStatus.Pending;

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Set when the organisation redeemed the code.</summary>
    public DateTimeOffset? RedeemedAt { get; set; }

    /// <summary>The grants created on approval, one per distinct category.</summary>
    public IReadOnlyList<Guid> GrantIds { get; set; } = [];

    /// <summary>
    /// True when the code can still be redeemed: it is pending and has not yet
    /// timed out. A code that has already been redeemed stays redeemable-in-
    /// principle only in the sense that the organisation may retry until the
    /// user acts - see <see cref="IsAwaitingApproval"/>.
    /// </summary>
    public bool IsRedeemable(DateTimeOffset now) =>
        Status is ShareCodeStatus.Pending && CodeExpiresAt > now;

    /// <summary>True when the organisation has redeemed and the user has not yet decided.</summary>
    public bool IsAwaitingApproval(DateTimeOffset now) =>
        Status is ShareCodeStatus.AwaitingApproval && CodeExpiresAt > now;
}

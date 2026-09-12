namespace VaultID.Services.Persistence;

/// <summary>
/// Lookup index that lets an organisation redeem a share code without knowing
/// which vault it belongs to. Event streams are per-vault, so redemption needs
/// a cross-vault index from the presented code to its owning stream.
/// <para>
/// Only the SHA-256 hash of the code is stored - the plaintext code exists
/// exactly once, in the response the user sees at generation time. A stolen
/// database therefore yields no usable codes.
/// </para>
/// </summary>
public sealed class ShareCodeIndexRecord
{
    /// <summary>Hex-encoded SHA-256 of the normalised code. Primary key.</summary>
    public required string CodeHash { get; init; }

    /// <summary>The vault (event stream) the code was generated from.</summary>
    public required string UserId { get; init; }

    /// <summary>Identifier of the <c>ShareCode</c> aggregate inside that stream.</summary>
    public required Guid ShareCodeId { get; init; }

    /// <summary>The only organisation permitted to redeem this code.</summary>
    public required string OrganisationId { get; init; }

    /// <summary>When the unredeemed code stops being usable.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

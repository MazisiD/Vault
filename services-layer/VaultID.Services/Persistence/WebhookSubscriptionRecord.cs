namespace VaultID.Services.Persistence;

/// <summary>
/// Persistence record for an organisation's webhook subscription. Used to
/// deliver change notifications ("PropagationSent") when a user updates a field
/// in a category the organisation can access.
/// </summary>
public sealed class WebhookSubscriptionRecord
{
    public required string Id { get; init; }
    public required string OrganisationId { get; init; }

    /// <summary>The vault/user whose changes the org wants to be notified about.</summary>
    public required string UserId { get; init; }

    /// <summary>Destination URL for the POST notification.</summary>
    public required string CallbackUrl { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

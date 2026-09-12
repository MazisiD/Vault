using VaultID.Services.Persistence;

namespace VaultID.Services.Abstractions;

/// <summary>
/// Data access for webhook subscriptions. Plain CRUD over
/// <see cref="WebhookSubscriptionRecord"/>.
/// </summary>
public interface IWebhookSubscriptionStore
{
    Task<WebhookSubscriptionRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task AddAsync(WebhookSubscriptionRecord record, CancellationToken cancellationToken = default);

    Task RemoveAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>All subscriptions registered for a given user/vault.</summary>
    Task<IReadOnlyList<WebhookSubscriptionRecord>> GetForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>All subscriptions registered by a given organisation.</summary>
    Task<IReadOnlyList<WebhookSubscriptionRecord>> GetForOrganisationAsync(string organisationId, CancellationToken cancellationToken = default);
}

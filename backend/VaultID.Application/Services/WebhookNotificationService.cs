using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Domain.Events;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Application.Services;

/// <summary>
/// Manages organisation change-subscriptions and the "update once, propagate
/// everywhere" mechanism (blueprint 5.3, 5.6).
/// <para>
/// On a field change, the orgs that currently hold an active share of the
/// affected category are determined, a PropagationSent audit event is recorded,
/// and a webhook is delivered to each subscribed org. Crucially the webhook
/// carries only the changed field NAME, not its value - the org must call the
/// API to read the value, so the permission check still applies.
/// </para>
/// </summary>
public sealed class WebhookNotificationService(
    VaultStreamRepository repository,
    IWebhookSubscriptionStore subscriptions)
{
    private readonly VaultStreamRepository _repository = repository;
    private readonly IWebhookSubscriptionStore _subscriptions = subscriptions;

    public async Task<WebhookSubscriptionView> SubscribeAsync(
        string organisationId, string userId, string callbackUrl, CancellationToken ct = default)
    {
        var record = new WebhookSubscriptionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            OrganisationId = organisationId,
            UserId = userId,
            CallbackUrl = callbackUrl
        };

        await _subscriptions.AddAsync(record, ct);
        return new WebhookSubscriptionView(record.Id, record.OrganisationId, record.UserId, record.CallbackUrl);
    }

    public Task UnsubscribeAsync(string subscriptionId, CancellationToken ct = default) =>
        _subscriptions.RemoveAsync(subscriptionId, ct);

    public async Task<IReadOnlyList<WebhookSubscriptionView>> ListForOrganisationAsync(
        string organisationId, CancellationToken ct = default)
    {
        var records = await _subscriptions.GetForOrganisationAsync(organisationId, ct);
        return records
            .Select(r => new WebhookSubscriptionView(r.Id, r.OrganisationId, r.UserId, r.CallbackUrl))
            .ToList();
    }

    /// <summary>
    /// Determines which organisations should be notified of a field change and
    /// records the propagation. Called by <see cref="VaultService"/> after a
    /// FieldUpdated event has been appended.
    /// </summary>
    public async Task PropagateFieldChangeAsync(
        string userId, Guid categoryId, Guid fieldDefinitionId, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;

        var recipientOrgIds = state.Grants.Values
            .Where(g => g.CategoryId == categoryId && g.IsCurrentlyActive(now))
            .Select(g => g.GranteeOrganisationId)
            .Distinct()
            .ToList();

        if (recipientOrgIds.Count == 0)
        {
            return;
        }

        var propagation = new PropagationSent
        {
            VaultId = userId,
            CategoryId = categoryId,
            ChangedFieldDefinitionId = fieldDefinitionId,
            NotifiedOrganisationIds = recipientOrgIds
        };

        await _repository.AppendAsync(userId, state.Version, [propagation], ct);

        // Deliver webhooks to subscribed organisations. The payload contains the
        // event type, user id and field name only (never the value).
        var subs = await _subscriptions.GetForUserAsync(userId, ct);
        foreach (var sub in subs.Where(s => recipientOrgIds.Contains(s.OrganisationId)))
        {
            // TODO: POST { type: "FieldChanged", userId, category, field } to
            // sub.CallbackUrl via a reliable queue with retry + dead-letter
            // (blueprint technical stack: "Custom service + Redis queue").
            _ = sub;
        }
    }
}

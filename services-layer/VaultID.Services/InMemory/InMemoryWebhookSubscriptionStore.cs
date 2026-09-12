using System.Collections.Concurrent;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Services.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IWebhookSubscriptionStore"/>.
/// <para>
/// TODO: Replace with a Supabase Postgres "webhook_subscriptions" table:
/// <code>
/// create table webhook_subscriptions (
///   id              text primary key,
///   organisation_id text not null,
///   user_id         text not null,
///   callback_url    text not null,
///   created_at      timestamptz not null default now()
/// );
/// create index on webhook_subscriptions (user_id);
/// create index on webhook_subscriptions (organisation_id);
/// </code>
/// </para>
/// </summary>
public sealed class InMemoryWebhookSubscriptionStore : IWebhookSubscriptionStore
{
    private readonly ConcurrentDictionary<string, WebhookSubscriptionRecord> _subs = new();

    public Task<WebhookSubscriptionRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "select * from webhook_subscriptions where id = @id".
        _subs.TryGetValue(id, out var record);
        return Task.FromResult(record);
    }

    public Task AddAsync(WebhookSubscriptionRecord record, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "insert into webhook_subscriptions (...) values (...)".
        _subs[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "delete from webhook_subscriptions where id = @id".
        _subs.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WebhookSubscriptionRecord>> GetForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "select * from webhook_subscriptions where user_id = @userId".
        IReadOnlyList<WebhookSubscriptionRecord> result = _subs.Values
            .Where(s => s.UserId == userId)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<WebhookSubscriptionRecord>> GetForOrganisationAsync(string organisationId, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "select * from webhook_subscriptions where organisation_id = @organisationId".
        IReadOnlyList<WebhookSubscriptionRecord> result = _subs.Values
            .Where(s => s.OrganisationId == organisationId)
            .ToList();
        return Task.FromResult(result);
    }
}

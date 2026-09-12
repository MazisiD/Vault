using Npgsql;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Services.Postgres;

/// <summary>
/// Postgres-backed <see cref="IWebhookSubscriptionStore"/> against the
/// `webhook_subscriptions` table (see supabase/data-schema.sql). Mirrors
/// <see cref="InMemory.InMemoryWebhookSubscriptionStore"/> exactly.
/// </summary>
public sealed class PostgresWebhookSubscriptionStore(NpgsqlDataSource dataSource) : IWebhookSubscriptionStore
{
    private const string SelectColumns = "id, organisation_id, user_id, callback_url, created_at";

    public async Task<WebhookSubscriptionRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {SelectColumns} from public.webhook_subscriptions where id = @id;";
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task AddAsync(WebhookSubscriptionRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into public.webhook_subscriptions (id, organisation_id, user_id, callback_url, created_at)
            values (@id, @organisationId, @userId, @callbackUrl, @createdAt);
            """;
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("organisationId", record.OrganisationId);
        command.Parameters.AddWithValue("userId", Guid.Parse(record.UserId));
        command.Parameters.AddWithValue("callbackUrl", record.CallbackUrl);
        command.Parameters.AddWithValue("createdAt", record.CreatedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from public.webhook_subscriptions where id = @id;";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookSubscriptionRecord>> GetForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {SelectColumns} from public.webhook_subscriptions where user_id = @userId;";
        command.Parameters.AddWithValue("userId", Guid.Parse(userId));
        return await ReadAllAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookSubscriptionRecord>> GetForOrganisationAsync(string organisationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {SelectColumns} from public.webhook_subscriptions where organisation_id = @organisationId;";
        command.Parameters.AddWithValue("organisationId", organisationId);
        return await ReadAllAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<WebhookSubscriptionRecord>> ReadAllAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<WebhookSubscriptionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    private static WebhookSubscriptionRecord Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetString(0),
        OrganisationId = reader.GetString(1),
        UserId = reader.GetGuid(2).ToString(),
        CallbackUrl = reader.GetString(3),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(4),
    };
}

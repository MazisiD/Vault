using Npgsql;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Services.Postgres;

/// <summary>
/// Postgres-backed <see cref="IEventStore"/> against the `events` table
/// (see supabase/data-schema.sql at the repo root) - the append-only source
/// of truth every vault's category/field schema, values, and grants are
/// replayed from. Mirrors <see cref="InMemory.InMemoryEventStore"/> exactly;
/// same semantics, real persistence.
/// </summary>
public sealed class PostgresEventStore(NpgsqlDataSource dataSource) : IEventStore
{
    public async Task<IReadOnlyList<StoredEvent>> AppendAsync(
        string streamId, long expectedVersion, IEnumerable<StoredEvent> events, CancellationToken cancellationToken = default)
    {
        var eventList = events.ToList();
        if (eventList.Count == 0)
        {
            return [];
        }

        var streamGuid = Guid.Parse(streamId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var appended = new List<StoredEvent>(eventList.Count);
        try
        {
            var version = expectedVersion;
            foreach (var e in eventList)
            {
                version++;

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    insert into public.events (id, stream_id, type, data, metadata, version, occurred_at)
                    values (@id, @streamId, @type, @data::jsonb, @metadata::jsonb, @version, @occurredAt)
                    returning global_position;
                    """;
                command.Parameters.AddWithValue("id", e.Id);
                command.Parameters.AddWithValue("streamId", streamGuid);
                command.Parameters.AddWithValue("type", e.Type);
                command.Parameters.AddWithValue("data", e.Data);
                command.Parameters.AddWithValue("metadata", (object?)e.Metadata ?? DBNull.Value);
                command.Parameters.AddWithValue("version", version);
                command.Parameters.AddWithValue("occurredAt", e.OccurredAt);

                var globalPosition = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
                appended.Add(new StoredEvent
                {
                    Id = e.Id,
                    StreamId = streamId,
                    Type = e.Type,
                    Data = e.Data,
                    Metadata = e.Metadata,
                    Version = version,
                    GlobalPosition = globalPosition,
                    OccurredAt = e.OccurredAt,
                });
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            var actualVersion = await GetStreamVersionAsync(streamId, cancellationToken);
            throw new ConcurrencyException(streamId, expectedVersion, actualVersion);
        }

        return appended;
    }

    public async Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(string streamId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select id, type, data, metadata, version, global_position, occurred_at
            from public.events
            where stream_id = @streamId
            order by version;
            """;
        command.Parameters.AddWithValue("streamId", Guid.Parse(streamId));

        var results = new List<StoredEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new StoredEvent
            {
                Id = reader.GetGuid(0),
                StreamId = streamId,
                Type = reader.GetString(1),
                Data = reader.GetString(2),
                Metadata = reader.IsDBNull(3) ? null : reader.GetString(3),
                Version = reader.GetInt64(4),
                GlobalPosition = reader.GetInt64(5),
                OccurredAt = reader.GetFieldValue<DateTimeOffset>(6),
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<StoredEvent>> ReadAllAsync(
        long fromGlobalPositionExclusive = 0, int maxCount = int.MaxValue, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select id, stream_id, type, data, metadata, version, global_position, occurred_at
            from public.events
            where global_position > @fromPosition
            order by global_position
            limit @limit;
            """;
        command.Parameters.AddWithValue("fromPosition", fromGlobalPositionExclusive);
        command.Parameters.AddWithValue("limit", maxCount);

        var results = new List<StoredEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new StoredEvent
            {
                Id = reader.GetGuid(0),
                StreamId = reader.GetGuid(1).ToString(),
                Type = reader.GetString(2),
                Data = reader.GetString(3),
                Metadata = reader.IsDBNull(4) ? null : reader.GetString(4),
                Version = reader.GetInt64(5),
                GlobalPosition = reader.GetInt64(6),
                OccurredAt = reader.GetFieldValue<DateTimeOffset>(7),
            });
        }

        return results;
    }

    public async Task<long> GetStreamVersionAsync(string streamId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from public.events where stream_id = @streamId;";
        command.Parameters.AddWithValue("streamId", Guid.Parse(streamId));
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}

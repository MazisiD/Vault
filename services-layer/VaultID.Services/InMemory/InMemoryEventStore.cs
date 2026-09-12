using System.Collections.Concurrent;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Services.InMemory;

/// <summary>
/// In-memory, thread-safe implementation of <see cref="IEventStore"/>.
/// <para>
/// This SIMULATES persistence using in-memory collections so the platform can
/// run end-to-end without a database. It is registered as a singleton so state
/// survives for the lifetime of the process.
/// </para>
/// <para>
/// TODO: Replace this in-memory implementation with a Supabase Postgres-backed
/// event store. Suggested schema:
/// <code>
/// create table events (
///   id              uuid primary key,
///   stream_id       text not null,
///   type            text not null,
///   data            jsonb not null,
///   metadata        jsonb,
///   version         bigint not null,
///   global_position bigserial,
///   occurred_at     timestamptz not null default now(),
///   unique (stream_id, version)          -- enforces optimistic concurrency
/// );
/// create index on events (stream_id, version);
/// </code>
/// The unique (stream_id, version) constraint gives the same optimistic
/// concurrency guarantee that <see cref="AppendAsync"/> enforces in memory.
/// </para>
/// </summary>
public sealed class InMemoryEventStore : IEventStore
{
    // Per-stream ordered event lists.
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _streams = new();
    // Global, cross-stream ordered log.
    private readonly List<StoredEvent> _global = [];
    private readonly object _gate = new();
    private long _globalPosition;

    public Task<IReadOnlyList<StoredEvent>> AppendAsync(
        string streamId,
        long expectedVersion,
        IEnumerable<StoredEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        // TODO: Replace with a Supabase Postgres transaction. The unique
        // (stream_id, version) constraint will surface concurrency conflicts;
        // translate the unique-violation error into a ConcurrencyException.
        lock (_gate)
        {
            var stream = _streams.GetOrAdd(streamId, _ => []);
            var actualVersion = stream.Count;

            if (actualVersion != expectedVersion)
            {
                throw new ConcurrencyException(streamId, expectedVersion, actualVersion);
            }

            var appended = new List<StoredEvent>();
            var version = actualVersion;

            foreach (var e in events)
            {
                version++;
                var persisted = new StoredEvent
                {
                    Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id,
                    StreamId = streamId,
                    Type = e.Type,
                    Data = e.Data,
                    Metadata = e.Metadata,
                    Version = version,
                    GlobalPosition = ++_globalPosition,
                    OccurredAt = e.OccurredAt == default ? DateTimeOffset.UtcNow : e.OccurredAt
                };

                stream.Add(persisted);
                _global.Add(persisted);
                appended.Add(persisted);
            }

            return Task.FromResult<IReadOnlyList<StoredEvent>>(appended);
        }
    }

    public Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "select * from events where stream_id = @id order by version".
        lock (_gate)
        {
            IReadOnlyList<StoredEvent> result = _streams.TryGetValue(streamId, out var stream)
                ? stream.ToList()
                : [];
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<StoredEvent>> ReadAllAsync(
        long fromGlobalPositionExclusive = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "select * from events where global_position > @pos
        //        order by global_position limit @maxCount".
        lock (_gate)
        {
            IReadOnlyList<StoredEvent> result = _global
                .Where(e => e.GlobalPosition > fromGlobalPositionExclusive)
                .OrderBy(e => e.GlobalPosition)
                .Take(maxCount)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<long> GetStreamVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "select coalesce(max(version), 0) from events where stream_id = @id".
        lock (_gate)
        {
            var version = _streams.TryGetValue(streamId, out var stream) ? stream.Count : 0;
            return Task.FromResult((long)version);
        }
    }
}

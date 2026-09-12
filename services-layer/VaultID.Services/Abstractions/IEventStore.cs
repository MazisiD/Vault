using VaultID.Services.Persistence;

namespace VaultID.Services.Abstractions;

/// <summary>
/// Append-only event store abstraction. This is the heart of the data-access
/// layer for the event-sourced parts of VaultID.
/// <para>
/// The store guarantees ordering within a stream (via optimistic concurrency on
/// the expected version) and exposes a global read for projections/activity
/// feeds. It contains no business logic - it only persists and returns
/// <see cref="StoredEvent"/> records.
/// </para>
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Appends events to the end of a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier (e.g. vault id).</param>
    /// <param name="expectedVersion">
    /// The version the caller believes the stream is currently at (0 for a new
    /// stream). If it does not match, a <see cref="ConcurrencyException"/> is
    /// thrown to protect against lost updates.
    /// </param>
    /// <param name="events">The events to append (already serialised).</param>
    /// <returns>The events as persisted, with versions/positions assigned.</returns>
    Task<IReadOnlyList<StoredEvent>> AppendAsync(
        string streamId,
        long expectedVersion,
        IEnumerable<StoredEvent> events,
        CancellationToken cancellationToken = default);

    /// <summary>Reads all events for a single stream in order.</summary>
    Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(
        string streamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads events across all streams from a global position (exclusive),
    /// ordered by global position. Used to build cross-cutting read models and
    /// activity feeds.
    /// </summary>
    Task<IReadOnlyList<StoredEvent>> ReadAllAsync(
        long fromGlobalPositionExclusive = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the current version (event count) of a stream; 0 if it does not exist.</summary>
    Task<long> GetStreamVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown when an append fails because the stream's actual version did not
/// match the expected version (optimistic concurrency conflict).
/// </summary>
public sealed class ConcurrencyException(string streamId, long expectedVersion, long actualVersion)
    : Exception($"Concurrency conflict on stream '{streamId}': expected version {expectedVersion} but found {actualVersion}.")
{
    public string StreamId { get; } = streamId;
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;
}

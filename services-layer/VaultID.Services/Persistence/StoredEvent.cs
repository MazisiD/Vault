namespace VaultID.Services.Persistence;

/// <summary>
/// An opaque, append-only event record as it lives in storage.
/// <para>
/// The data-access layer deliberately knows NOTHING about the meaning of an
/// event. It stores a stream id, a string type discriminator and a serialised
/// payload. The backend (Application layer) is responsible for serialising
/// domain events into this shape and rehydrating them. This keeps the service
/// layer fully independent of the domain model and therefore reusable.
/// </para>
/// </summary>
public sealed class StoredEvent
{
    /// <summary>Globally unique id of this event record.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The stream this event belongs to (e.g. a vault id). Events are ordered
    /// per stream by <see cref="Version"/>.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>String discriminator for the event type (e.g. "FieldUpdated").</summary>
    public required string Type { get; init; }

    /// <summary>Serialised event payload (JSON). Opaque to this layer.</summary>
    public required string Data { get; init; }

    /// <summary>Optional serialised metadata (JSON) - signatures, correlation ids, etc.</summary>
    public string? Metadata { get; init; }

    /// <summary>1-based position of this event within its stream.</summary>
    public long Version { get; init; }

    /// <summary>Monotonic global position across all streams (assigned by the store).</summary>
    public long GlobalPosition { get; init; }

    /// <summary>When the event was appended (UTC).</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

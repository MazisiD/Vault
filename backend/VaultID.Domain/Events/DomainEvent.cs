using System.Text.Json.Serialization;

namespace VaultID.Domain.Events;

/// <summary>
/// Base type for every event in the system. VaultID is built on event sourcing
/// (blueprint 4.3): the current state of a vault is derived by replaying these
/// immutable events in order. Every share, access and revocation is one of
/// these.
/// <para>
/// Events are intentionally simple, serialisable data carriers. They carry no
/// behaviour; the Application layer applies them to rebuild state.
/// </para>
/// </summary>
public abstract record DomainEvent
{
    /// <summary>Unique id of this event occurrence.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The vault (stream) the event belongs to. For a user vault this is the
    /// user id; all of a user's events form one ordered stream.
    /// </summary>
    public required string VaultId { get; init; }

    /// <summary>When the event occurred (UTC).</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Stable string discriminator used when (de)serialising to the event store.
    /// Equal to the concrete type name.
    /// </summary>
    [JsonIgnore]
    public string EventType => GetType().Name;
}

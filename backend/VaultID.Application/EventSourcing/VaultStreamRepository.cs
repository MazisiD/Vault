using VaultID.Domain.Events;
using VaultID.Domain.Models;
using VaultID.Services.Abstractions;

namespace VaultID.Application.EventSourcing;

/// <summary>
/// Loads and saves vault event streams. Bridges the Application layer to the
/// data-access layer's <see cref="IEventStore"/>, handling serialisation and
/// optimistic concurrency. The rest of the business logic deals in domain
/// events and <see cref="VaultState"/>, never in stored records.
/// </summary>
public sealed class VaultStreamRepository(IEventStore eventStore, EventSerializer serializer)
{
    private readonly IEventStore _eventStore = eventStore;
    private readonly EventSerializer _serializer = serializer;

    /// <summary>Loads the full event stream for a vault as domain events.</summary>
    public async Task<IReadOnlyList<DomainEvent>> LoadEventsAsync(string userId, CancellationToken ct = default)
    {
        var stored = await _eventStore.ReadStreamAsync(userId, ct);
        return stored.Select(_serializer.Deserialize).ToList();
    }

    /// <summary>Loads and projects the current state of a vault.</summary>
    public async Task<VaultState> LoadStateAsync(string userId, CancellationToken ct = default)
    {
        var events = await LoadEventsAsync(userId, ct);
        return VaultProjector.Project(userId, events);
    }

    /// <summary>
    /// Appends new events to a vault stream using optimistic concurrency.
    /// <paramref name="expectedVersion"/> is the version the caller projected
    /// state from; a mismatch throws <see cref="ConcurrencyException"/>.
    /// </summary>
    public async Task AppendAsync(
        string userId,
        long expectedVersion,
        IReadOnlyList<DomainEvent> events,
        CancellationToken ct = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var stored = events.Select(_serializer.Serialize);
                await _eventStore.AppendAsync(userId, expectedVersion, stored, ct);
                return;
            }
            catch (ConcurrencyException) when (attempt < 2)
            {
                expectedVersion = await _eventStore.GetStreamVersionAsync(userId, ct);
            }
        }
    }
}

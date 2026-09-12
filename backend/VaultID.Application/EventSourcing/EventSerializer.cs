using System.Text.Json;
using VaultID.Domain.Events;
using VaultID.Services.Persistence;

namespace VaultID.Application.EventSourcing;

/// <summary>
/// Translates between rich <see cref="DomainEvent"/> instances (Domain layer)
/// and the opaque <see cref="StoredEvent"/> records (Services layer).
/// <para>
/// This is the seam that lets the data-access layer remain ignorant of the
/// domain model: the Application layer owns the mapping. Each event type is
/// registered by name; the name is stored in <see cref="StoredEvent.Type"/> and
/// used to pick the correct CLR type when rehydrating.
/// </para>
/// </summary>
public sealed class EventSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // Registry of event-type name -> CLR type. Keeping it explicit (rather than
    // reflection-scanning) makes the serialisation contract obvious and stable.
    private static readonly IReadOnlyDictionary<string, Type> EventTypes =
        new Dictionary<string, Type>
        {
            [nameof(VaultCreated)] = typeof(VaultCreated),
            [nameof(FieldUpdated)] = typeof(FieldUpdated),
            [nameof(CategoryCreated)] = typeof(CategoryCreated),
            [nameof(CategoryRenamed)] = typeof(CategoryRenamed),
            [nameof(CategoryDeleted)] = typeof(CategoryDeleted),
            [nameof(FieldDefinitionCreated)] = typeof(FieldDefinitionCreated),
            [nameof(FieldDefinitionUpdated)] = typeof(FieldDefinitionUpdated),
            [nameof(FieldDefinitionDeleted)] = typeof(FieldDefinitionDeleted),
            [nameof(AgreementPresented)] = typeof(AgreementPresented),
            [nameof(AgreementSigned)] = typeof(AgreementSigned),
            [nameof(CategoryShared)] = typeof(CategoryShared),
            [nameof(DataAccessed)] = typeof(DataAccessed),
            [nameof(ShareRevoked)] = typeof(ShareRevoked),
            [nameof(ShareExpired)] = typeof(ShareExpired),
            [nameof(RenewalRequested)] = typeof(RenewalRequested),
            [nameof(AccessDenied)] = typeof(AccessDenied),
            [nameof(PropagationSent)] = typeof(PropagationSent),
            [nameof(ConsentRenewed)] = typeof(ConsentRenewed),
            [nameof(ShareExpiryChanged)] = typeof(ShareExpiryChanged),
            [nameof(ShareCodeGenerated)] = typeof(ShareCodeGenerated),
            [nameof(ShareCodeRedeemed)] = typeof(ShareCodeRedeemed),
            [nameof(ShareCodeApproved)] = typeof(ShareCodeApproved),
            [nameof(ShareCodeRejected)] = typeof(ShareCodeRejected),
            [nameof(ShareCodeRevoked)] = typeof(ShareCodeRevoked),
            [nameof(ShareCodeExpired)] = typeof(ShareCodeExpired)
        };

    /// <summary>Serialises a domain event into a storable record.</summary>
    public StoredEvent Serialize(DomainEvent @event)
    {
        var type = @event.GetType();
        var data = JsonSerializer.Serialize(@event, type, Options);

        return new StoredEvent
        {
            Id = @event.EventId,
            StreamId = @event.VaultId,
            Type = @event.EventType,
            Data = data,
            OccurredAt = @event.OccurredAt
        };
    }

    /// <summary>Rehydrates a stored record back into its domain event type.</summary>
    public DomainEvent Deserialize(StoredEvent stored)
    {
        if (!EventTypes.TryGetValue(stored.Type, out var clrType))
        {
            throw new InvalidOperationException($"Unknown event type '{stored.Type}'. Register it in {nameof(EventSerializer)}.");
        }

        var @event = (DomainEvent?)JsonSerializer.Deserialize(stored.Data, clrType, Options);
        return @event ?? throw new InvalidOperationException($"Failed to deserialise event '{stored.Type}'.");
    }
}

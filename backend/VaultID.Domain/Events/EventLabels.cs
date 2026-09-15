using System.Text;

namespace VaultID.Domain.Events;

/// <summary>
/// Turns an event's type discriminator into the sentence-case label the
/// activity feed shows, e.g. <c>FieldUpdated</c> becomes "Field updated" and
/// <c>ShareCodeGenerated</c> becomes "Share code generated".
/// <para>
/// The PascalCase discriminator stays exactly as it is in the event store - it
/// is the key the serialiser maps back to a CLR type, so it can never be
/// reformatted. Nothing user-facing should print it raw; read
/// <see cref="DomainEvent.EventLabel"/> instead.
/// </para>
/// </summary>
public static class EventLabels
{
    /// <summary>Splits a PascalCase event type name into a readable label.</summary>
    public static string ForEventType(string eventType)
    {
        if (string.IsNullOrEmpty(eventType))
        {
            return eventType;
        }

        var label = new StringBuilder(eventType.Length + 4);
        label.Append(char.ToUpperInvariant(eventType[0]));

        for (var i = 1; i < eventType.Length; i++)
        {
            var c = eventType[i];
            if (char.IsUpper(c))
            {
                label.Append(' ');
                label.Append(char.ToLowerInvariant(c));
            }
            else
            {
                label.Append(c);
            }
        }

        return label.ToString();
    }
}

using VaultID.Domain.Events;
using Xunit;

namespace VaultID.Tests.Domain;

/// <summary>
/// The PascalCase type name is a storage discriminator, never a display string.
/// Anything user-facing reads the label instead.
/// </summary>
public sealed class EventLabelsTests
{
    [Theory]
    [InlineData("FieldUpdated", "Field updated")]
    [InlineData("VaultCreated", "Vault created")]
    [InlineData("FieldDefinitionDeleted", "Field definition deleted")]
    [InlineData("ShareCodeGenerated", "Share code generated")]
    [InlineData("AccessDenied", "Access denied")]
    public void ForEventType_ReadsAsASentence(string eventType, string expected) =>
        Assert.Equal(expected, EventLabels.ForEventType(eventType));

    [Fact]
    public void AnEventLabelsItself()
    {
        var updated = new FieldUpdated
        {
            VaultId = "user-1",
            FieldDefinitionId = Guid.NewGuid(),
            NewValue = "Jane Doe"
        };

        Assert.Equal("FieldUpdated", updated.EventType);
        Assert.Equal("Field updated", updated.EventLabel);
    }
}

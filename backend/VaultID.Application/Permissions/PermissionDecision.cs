using VaultID.Domain;
using VaultID.Domain.Models;

namespace VaultID.Application.Permissions;

/// <summary>Result of evaluating an organisation's access request.</summary>
public sealed record PermissionDecision
{
    public required bool Allowed { get; init; }

    /// <summary>The matched active grant when <see cref="Allowed"/> is true.</summary>
    public PermissionGrant? Grant { get; init; }

    /// <summary>Human-readable reason when denied (also logged as AccessDenied).</summary>
    public string? DenialReason { get; init; }

    public static PermissionDecision Allow(PermissionGrant grant) =>
        new() { Allowed = true, Grant = grant };

    public static PermissionDecision Deny(string reason) =>
        new() { Allowed = false, DenialReason = reason };
}

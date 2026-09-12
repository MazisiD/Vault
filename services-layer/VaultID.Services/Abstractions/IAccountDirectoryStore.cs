namespace VaultID.Services.Abstractions;

/// <summary>
/// Read-only lookup against the identity provider's user directory (Supabase
/// Auth plus the `profiles` table - see supabase/schema.sql at the repo root).
/// Used for account-recovery flows Supabase Auth doesn't provide natively,
/// e.g. "forgot username" (Supabase Auth has no username concept).
/// </summary>
public interface IAccountDirectoryStore
{
    /// <summary>Looks up the username registered against an email, or null if no account matches.</summary>
    Task<string?> GetUsernameForEmailAsync(string email, CancellationToken cancellationToken = default);
}

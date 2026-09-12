using VaultID.Services.Persistence;

namespace VaultID.Services.Abstractions;

/// <summary>
/// Data access for the cross-vault share-code lookup index. Lets the
/// organisation-facing API resolve a presented code to the vault that issued
/// it. Lookups are by hash only; the plaintext code is never persisted.
/// </summary>
public interface IShareCodeIndexStore
{
    Task AddAsync(ShareCodeIndexRecord record, CancellationToken cancellationToken = default);

    /// <summary>Resolves a hashed code to its issuing vault, or null if unknown.</summary>
    Task<ShareCodeIndexRecord?> FindByHashAsync(string codeHash, CancellationToken cancellationToken = default);

    /// <summary>Removes the index entry once the code is spent (approved, rejected or revoked).</summary>
    Task RemoveAsync(string codeHash, CancellationToken cancellationToken = default);
}

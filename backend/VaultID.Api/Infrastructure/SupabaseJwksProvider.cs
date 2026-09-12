using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace VaultID.Api.Infrastructure;

/// <summary>
/// Fetches and caches Supabase Auth's public signing keys
/// ("{Supabase:Url}/auth/v1/.well-known/jwks.json"). Newer Supabase projects
/// sign session JWTs with an asymmetric key (ES256), not a shared secret, so
/// validating them means checking the signature against this public key
/// rather than a symmetric secret. Refreshes periodically so a key rotation
/// on Supabase's side doesn't require restarting the backend.
/// </summary>
public sealed class SupabaseJwksProvider(HttpClient httpClient, IOptions<SupabaseOptions> options)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<SecurityKey> _keys = [];
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(CancellationToken ct = default)
    {
        if (IsFresh())
        {
            return _keys;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (IsFresh())
            {
                return _keys;
            }

            var url = $"{options.Value.Url.TrimEnd('/')}/auth/v1/.well-known/jwks.json";
            var json = await httpClient.GetStringAsync(url, ct);
            _keys = new JsonWebKeySet(json).GetSigningKeys().ToList();
            _fetchedAt = DateTimeOffset.UtcNow;
            return _keys;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsFresh() => _keys.Count > 0 && DateTimeOffset.UtcNow - _fetchedAt < CacheDuration;
}

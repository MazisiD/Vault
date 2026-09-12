using System.Collections.Concurrent;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Services.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IShareCodeIndexStore"/>.
/// <para>
/// TODO: Replace with a Supabase Postgres "share_code_index" table:
/// <code>
/// create table share_code_index (
///   code_hash       text primary key,
///   user_id         uuid not null,
///   share_code_id   uuid not null,
///   organisation_id text not null,
///   expires_at      timestamptz not null,
///   created_at      timestamptz not null default now()
/// );
/// create index on share_code_index (expires_at);
/// </code>
/// </para>
/// </summary>
public sealed class InMemoryShareCodeIndexStore : IShareCodeIndexStore
{
    private readonly ConcurrentDictionary<string, ShareCodeIndexRecord> _index = new(StringComparer.Ordinal);

    public Task AddAsync(ShareCodeIndexRecord record, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "insert into share_code_index (...) values (...)".
        _index[record.CodeHash] = record;
        return Task.CompletedTask;
    }

    public Task<ShareCodeIndexRecord?> FindByHashAsync(string codeHash, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "select * from share_code_index where code_hash = @codeHash".
        _index.TryGetValue(codeHash, out var record);
        return Task.FromResult(record);
    }

    public Task RemoveAsync(string codeHash, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "delete from share_code_index where code_hash = @codeHash".
        _index.TryRemove(codeHash, out _);
        return Task.CompletedTask;
    }
}

using System.Collections.Concurrent;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Services.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IOrganisationStore"/>.
/// <para>
/// TODO: Replace with a Supabase Postgres "organisations" table. Suggested schema:
/// <code>
/// create table organisations (
///   id                            text primary key,
///   name                          text not null,
///   status                        text not null default 'pending',
///   agreement_id                  text,
///   agreement_purpose             text,
///   agreement_retention_days      int,
///   agreement_legal_basis         text,
///   agreement_third_party_sharing text,
///   agreement_deletion_commitment text,
///   registered_at                 timestamptz not null default now(),
///   updated_at                    timestamptz not null default now()
/// );
/// </code>
/// </para>
/// </summary>
public sealed class InMemoryOrganisationStore : IOrganisationStore
{
    private readonly ConcurrentDictionary<string, OrganisationRecord> _orgs = new(StringComparer.OrdinalIgnoreCase);

    public Task<OrganisationRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "select * from organisations where id = @id".
        _orgs.TryGetValue(id, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<OrganisationRecord>> SearchAsync(string? nameQuery, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with "select * from organisations where name ilike '%' || @q || '%'".
        IReadOnlyList<OrganisationRecord> result = _orgs.Values
            .Where(o => string.IsNullOrWhiteSpace(nameQuery)
                        || o.Name.Contains(nameQuery, StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => o.Name)
            .ToList();
        return Task.FromResult(result);
    }

    public Task UpsertAsync(OrganisationRecord record, CancellationToken cancellationToken = default)
    {
        // TODO: Replace with an "insert ... on conflict (id) do update" statement.
        record.UpdatedAt = DateTimeOffset.UtcNow;
        _orgs[record.Id] = record;
        return Task.CompletedTask;
    }
}

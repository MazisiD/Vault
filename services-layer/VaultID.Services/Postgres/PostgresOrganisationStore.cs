using Npgsql;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Services.Postgres;

/// <summary>
/// Postgres-backed <see cref="IOrganisationStore"/> against the
/// `organisations` table (see supabase/data-schema.sql). Mirrors
/// <see cref="InMemory.InMemoryOrganisationStore"/> exactly.
/// </summary>
public sealed class PostgresOrganisationStore(NpgsqlDataSource dataSource) : IOrganisationStore
{
    private const string SelectColumns = """
        id, name, status, agreement_id, agreement_purpose, agreement_retention_days,
        agreement_legal_basis, agreement_third_party_sharing, agreement_deletion_commitment,
        registered_at, updated_at
        """;

    public async Task<OrganisationRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {SelectColumns} from public.organisations where id = @id;";
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<OrganisationRecord>> SearchAsync(string? nameQuery, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select {SelectColumns} from public.organisations
            where @query::text is null or name ilike '%' || @query::text || '%'
            order by name;
            """;
        command.Parameters.AddWithValue("query", (object?)nameQuery ?? DBNull.Value);

        var results = new List<OrganisationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpsertAsync(OrganisationRecord record, CancellationToken cancellationToken = default)
    {
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into public.organisations
                (id, name, status, agreement_id, agreement_purpose, agreement_retention_days,
                 agreement_legal_basis, agreement_third_party_sharing, agreement_deletion_commitment,
                 registered_at, updated_at)
            values
                (@id, @name, @status, @agreementId, @agreementPurpose, @agreementRetentionDays,
                 @agreementLegalBasis, @agreementThirdPartySharing, @agreementDeletionCommitment,
                 @registeredAt, @updatedAt)
            on conflict (id) do update set
                name = excluded.name,
                status = excluded.status,
                agreement_id = excluded.agreement_id,
                agreement_purpose = excluded.agreement_purpose,
                agreement_retention_days = excluded.agreement_retention_days,
                agreement_legal_basis = excluded.agreement_legal_basis,
                agreement_third_party_sharing = excluded.agreement_third_party_sharing,
                agreement_deletion_commitment = excluded.agreement_deletion_commitment,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("name", record.Name);
        command.Parameters.AddWithValue("status", record.Status);
        command.Parameters.AddWithValue("agreementId", (object?)record.AgreementId ?? DBNull.Value);
        command.Parameters.AddWithValue("agreementPurpose", (object?)record.AgreementPurpose ?? DBNull.Value);
        command.Parameters.AddWithValue("agreementRetentionDays", (object?)record.AgreementRetentionDays ?? DBNull.Value);
        command.Parameters.AddWithValue("agreementLegalBasis", (object?)record.AgreementLegalBasis ?? DBNull.Value);
        command.Parameters.AddWithValue("agreementThirdPartySharing", (object?)record.AgreementThirdPartySharing ?? DBNull.Value);
        command.Parameters.AddWithValue("agreementDeletionCommitment", (object?)record.AgreementDeletionCommitment ?? DBNull.Value);
        command.Parameters.AddWithValue("registeredAt", record.RegisteredAt);
        command.Parameters.AddWithValue("updatedAt", record.UpdatedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static OrganisationRecord Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Status = reader.GetString(2),
        AgreementId = reader.IsDBNull(3) ? null : reader.GetString(3),
        AgreementPurpose = reader.IsDBNull(4) ? null : reader.GetString(4),
        AgreementRetentionDays = reader.IsDBNull(5) ? null : reader.GetInt32(5),
        AgreementLegalBasis = reader.IsDBNull(6) ? null : reader.GetString(6),
        AgreementThirdPartySharing = reader.IsDBNull(7) ? null : reader.GetString(7),
        AgreementDeletionCommitment = reader.IsDBNull(8) ? null : reader.GetString(8),
        RegisteredAt = reader.GetFieldValue<DateTimeOffset>(9),
        UpdatedAt = reader.GetFieldValue<DateTimeOffset>(10),
    };
}

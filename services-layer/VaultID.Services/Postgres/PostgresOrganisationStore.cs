using System.Text.Json;
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
        registration_number, address, industry, contact_name, contact_phone, contact_email,
        category_agreements, pending_invites,
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
                 registration_number, address, industry, contact_name, contact_phone, contact_email,
                 category_agreements, pending_invites,
                 registered_at, updated_at)
            values
                (@id, @name, @status, @agreementId, @agreementPurpose, @agreementRetentionDays,
                 @agreementLegalBasis, @agreementThirdPartySharing, @agreementDeletionCommitment,
                 @registrationNumber, @address, @industry, @contactName, @contactPhone, @contactEmail,
                 @categoryAgreements::jsonb, @pendingInvites::jsonb,
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
                registration_number = excluded.registration_number,
                address = excluded.address,
                industry = excluded.industry,
                contact_name = excluded.contact_name,
                contact_phone = excluded.contact_phone,
                contact_email = excluded.contact_email,
                category_agreements = excluded.category_agreements,
                pending_invites = excluded.pending_invites,
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
        command.Parameters.AddWithValue("registrationNumber", (object?)record.RegistrationNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("address", (object?)record.Address ?? DBNull.Value);
        command.Parameters.AddWithValue("industry", (object?)record.Industry ?? DBNull.Value);
        command.Parameters.AddWithValue("contactName", (object?)record.ContactName ?? DBNull.Value);
        command.Parameters.AddWithValue("contactPhone", (object?)record.ContactPhone ?? DBNull.Value);
        command.Parameters.AddWithValue("contactEmail", (object?)record.ContactEmail ?? DBNull.Value);
        command.Parameters.AddWithValue("categoryAgreements", JsonSerializer.Serialize(record.CategoryAgreements));
        command.Parameters.AddWithValue("pendingInvites", JsonSerializer.Serialize(record.PendingInvites));
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
        RegistrationNumber = reader.IsDBNull(9) ? null : reader.GetString(9),
        Address = reader.IsDBNull(10) ? null : reader.GetString(10),
        Industry = reader.IsDBNull(11) ? null : reader.GetString(11),
        ContactName = reader.IsDBNull(12) ? null : reader.GetString(12),
        ContactPhone = reader.IsDBNull(13) ? null : reader.GetString(13),
        ContactEmail = reader.IsDBNull(14) ? null : reader.GetString(14),
        CategoryAgreements = reader.IsDBNull(15)
            ? new Dictionary<string, CategoryAgreementRecord>(StringComparer.OrdinalIgnoreCase)
            : JsonSerializer.Deserialize<Dictionary<string, CategoryAgreementRecord>>(reader.GetString(15))
                ?? new Dictionary<string, CategoryAgreementRecord>(StringComparer.OrdinalIgnoreCase),
        PendingInvites = reader.IsDBNull(16)
            ? []
            : JsonSerializer.Deserialize<List<string>>(reader.GetString(16)) ?? [],
        RegisteredAt = reader.GetFieldValue<DateTimeOffset>(17),
        UpdatedAt = reader.GetFieldValue<DateTimeOffset>(18),
    };
}

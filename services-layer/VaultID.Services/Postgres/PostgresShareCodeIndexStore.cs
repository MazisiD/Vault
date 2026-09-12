using Npgsql;
using VaultID.Services.Abstractions;
using VaultID.Services.Persistence;

namespace VaultID.Services.Postgres;

/// <summary>
/// Postgres-backed <see cref="IShareCodeIndexStore"/> against the
/// `share_code_index` table (see supabase/data-schema.sql). Mirrors
/// <see cref="InMemory.InMemoryShareCodeIndexStore"/> exactly.
/// </summary>
public sealed class PostgresShareCodeIndexStore(NpgsqlDataSource dataSource) : IShareCodeIndexStore
{
    private const string SelectColumns = "code_hash, user_id, share_code_id, organisation_id, expires_at, created_at";

    public async Task AddAsync(ShareCodeIndexRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into public.share_code_index (code_hash, user_id, share_code_id, organisation_id, expires_at, created_at)
            values (@codeHash, @userId, @shareCodeId, @organisationId, @expiresAt, @createdAt);
            """;
        command.Parameters.AddWithValue("codeHash", record.CodeHash);
        command.Parameters.AddWithValue("userId", Guid.Parse(record.UserId));
        command.Parameters.AddWithValue("shareCodeId", record.ShareCodeId);
        command.Parameters.AddWithValue("organisationId", record.OrganisationId);
        command.Parameters.AddWithValue("expiresAt", record.ExpiresAt);
        command.Parameters.AddWithValue("createdAt", record.CreatedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ShareCodeIndexRecord?> FindByHashAsync(string codeHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {SelectColumns} from public.share_code_index where code_hash = @codeHash;";
        command.Parameters.AddWithValue("codeHash", codeHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task RemoveAsync(string codeHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "delete from public.share_code_index where code_hash = @codeHash;";
        command.Parameters.AddWithValue("codeHash", codeHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ShareCodeIndexRecord Map(NpgsqlDataReader reader) => new()
    {
        CodeHash = reader.GetString(0),
        UserId = reader.GetGuid(1).ToString(),
        ShareCodeId = reader.GetGuid(2),
        OrganisationId = reader.GetString(3),
        ExpiresAt = reader.GetFieldValue<DateTimeOffset>(4),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
    };
}

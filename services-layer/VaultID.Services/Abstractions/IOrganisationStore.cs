using VaultID.Services.Persistence;

namespace VaultID.Services.Abstractions;

/// <summary>
/// Data access for the organisation registry. Plain CRUD over
/// <see cref="OrganisationRecord"/> - no validation or compliance rules (those
/// live in the backend).
/// </summary>
public interface IOrganisationStore
{
    Task<OrganisationRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganisationRecord>> SearchAsync(string? nameQuery, CancellationToken cancellationToken = default);

    Task UpsertAsync(OrganisationRecord record, CancellationToken cancellationToken = default);
}

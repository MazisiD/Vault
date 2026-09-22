using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Application.Permissions;
using VaultID.Domain;
using VaultID.Domain.Events;
using VaultID.Domain.Models;

namespace VaultID.Application.Services;

/// <summary>
/// The organisation-facing data access use cases - the "reference, not copy"
/// query path (blueprint 5.5). Every request passes through the permission
/// engine and is logged: success as DataAccessed, denial as AccessDenied. The
/// org always receives live data, never a stored copy.
/// </summary>
public sealed class OrganisationDataAccessService(
    VaultStreamRepository repository,
    PermissionEngine permissionEngine)
{
    private readonly VaultStreamRepository _repository = repository;
    private readonly PermissionEngine _engine = permissionEngine;

    /// <summary>Returns all fields in a shared category, or a denial.</summary>
    public async Task<DataQueryResult> QueryCategoryAsync(
        string userId, string organisationId, Guid categoryId, string? ipAddress, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;
        var decision = _engine.Evaluate(state, organisationId, categoryId, now);

        if (!decision.Allowed)
        {
            await LogDeniedAsync(userId, organisationId, categoryId, [], decision.DenialReason!, state.Version, ct);
            return new DataQueryResult(false, decision.DenialReason, categoryId, new Dictionary<Guid, string?>());
        }

        var fields = state.GetCategoryValues(categoryId);

        // An organisation may hold several grants over one category, each
        // covering a different slice of it, so the read returns the union of
        // everything currently permitted rather than one grant's slice.
        var permitted = PermittedFields(state, organisationId, categoryId, now);
        if (permitted is not null)
        {
            fields = fields
                .Where(kvp => permitted.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        await LogAccessedAsync(userId, organisationId, categoryId, fields.Keys.ToList(), ipAddress, state.Version, ct);
        return new DataQueryResult(true, null, categoryId, fields, FieldNamesFor(state, fields.Keys));
    }

    /// <summary>Returns a single field, resolving its owning category via the field's own FK, or a denial.</summary>
    public async Task<DataQueryResult> QueryFieldAsync(
        string userId, string organisationId, Guid fieldDefinitionId, string? ipAddress, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);

        if (!state.FieldDefinitions.TryGetValue(fieldDefinitionId, out var field))
        {
            return new DataQueryResult(false, $"Unknown field '{fieldDefinitionId}'.", Guid.Empty, new Dictionary<Guid, string?>());
        }

        var categoryId = field.CategoryId;
        var now = DateTimeOffset.UtcNow;
        var decision = _engine.Evaluate(state, organisationId, categoryId, now, fieldDefinitionId);

        if (!decision.Allowed)
        {
            await LogDeniedAsync(userId, organisationId, categoryId, [fieldDefinitionId], decision.DenialReason!, state.Version, ct);
            return new DataQueryResult(false, decision.DenialReason, categoryId, new Dictionary<Guid, string?>());
        }

        var values = state.GetCategoryValues(categoryId);
        var result = new Dictionary<Guid, string?>();
        if (values.TryGetValue(fieldDefinitionId, out var value))
        {
            result[fieldDefinitionId] = value;
        }

        await LogAccessedAsync(userId, organisationId, categoryId, [fieldDefinitionId], ipAddress, state.Version, ct);
        return new DataQueryResult(true, null, categoryId, result, FieldNamesFor(state, result.Keys));
    }

    /// <summary>
    /// Verifies a field value without revealing it (blueprint API
    /// /verify/{field}). Returns only true/false. Still permission-checked and
    /// logged as a DataAccessed event.
    /// </summary>
    public async Task<(bool Allowed, string? DenialReason, bool Matches)> VerifyFieldAsync(
        string userId, string organisationId, Guid fieldDefinitionId, string candidateValue, string? ipAddress, CancellationToken ct = default)
    {
        var state = await _repository.LoadStateAsync(userId, ct);

        if (!state.FieldDefinitions.TryGetValue(fieldDefinitionId, out var field))
        {
            return (false, $"Unknown field '{fieldDefinitionId}'.", false);
        }

        var categoryId = field.CategoryId;
        var now = DateTimeOffset.UtcNow;
        var decision = _engine.Evaluate(state, organisationId, categoryId, now, fieldDefinitionId);

        if (!decision.Allowed)
        {
            await LogDeniedAsync(userId, organisationId, categoryId, [fieldDefinitionId], decision.DenialReason!, state.Version, ct);
            return (false, decision.DenialReason, false);
        }

        var values = state.GetCategoryValues(categoryId);
        var matches = values.TryGetValue(fieldDefinitionId, out var stored) &&
                      string.Equals(stored, candidateValue, StringComparison.Ordinal);

        await LogAccessedAsync(userId, organisationId, categoryId, [fieldDefinitionId], ipAddress, state.Version, ct);
        return (true, null, matches);
    }

    /// <summary>
    /// The union of fields the organisation may currently read from a category,
    /// or <c>null</c> when it holds an unrestricted grant and may read all of it.
    /// </summary>
    private static HashSet<Guid>? PermittedFields(
        VaultState state, string organisationId, Guid categoryId, DateTimeOffset now)
    {
        var live = state.Grants.Values
            .Where(g => g.GranteeOrganisationId == organisationId &&
                        g.CategoryId == categoryId &&
                        g.Status is GrantStatus.Active or GrantStatus.PendingRenewal &&
                        (g.ExpiresAt is null || g.ExpiresAt > now))
            .ToList();

        if (live.Any(g => g.FieldDefinitionIds is null))
        {
            return null;
        }

        return live.SelectMany(g => g.FieldDefinitionIds!).ToHashSet();
    }

    private static IReadOnlyDictionary<Guid, string> FieldNamesFor(VaultState state, IEnumerable<Guid> fieldDefinitionIds) =>
        fieldDefinitionIds
            .Where(state.FieldDefinitions.ContainsKey)
            .ToDictionary(id => id, id => state.FieldDefinitions[id].Name);

    private async Task LogAccessedAsync(
        string userId, string organisationId, Guid categoryId,
        IReadOnlyList<Guid> fields, string? ip, long version, CancellationToken ct)
    {
        var accessed = new DataAccessed
        {
            VaultId = userId,
            OrganisationId = organisationId,
            CategoryId = categoryId,
            FieldsRead = fields,
            IpAddress = ip
        };

        await _repository.AppendAsync(userId, version, [accessed], ct);
    }

    private async Task LogDeniedAsync(
        string userId, string organisationId, Guid? categoryId,
        IReadOnlyList<Guid> attemptedFields, string reason, long version, CancellationToken ct)
    {
        var denied = new AccessDenied
        {
            VaultId = userId,
            OrganisationId = organisationId,
            CategoryId = categoryId,
            AttemptedFields = attemptedFields,
            Reason = reason
        };

        await _repository.AppendAsync(userId, version, [denied], ct);
    }
}

using VaultID.Application.Contracts;
using VaultID.Application.EventSourcing;
using VaultID.Domain.Events;
using VaultID.Domain.Models;

namespace VaultID.Application.Services;

/// <summary>
/// Builds the transparent activity feed (blueprint 4.3, 1): a human-readable
/// timeline of every event on a vault - who accessed data and when, every
/// share, revocation and expiry. Derived entirely from the event stream.
/// <para>
/// Category/field names are resolved against the vault's current schema
/// projection (rather than persisted per-event) for a best-effort readable
/// summary; a category or field that has since been renamed or deleted shows
/// its most recent known name, falling back to its id.
/// </para>
/// </summary>
public sealed class ActivityService(VaultStreamRepository repository)
{
    private readonly VaultStreamRepository _repository = repository;

    /// <summary>Returns the activity feed, newest first.</summary>
    public async Task<IReadOnlyList<ActivityEntry>> GetFeedAsync(string userId, CancellationToken ct = default)
    {
        var events = await _repository.LoadEventsAsync(userId, ct);
        var state = VaultProjector.Project(userId, events);

        return events
            .Select(e => ToEntry(e, state))
            .OrderByDescending(e => e.OccurredAt)
            .ToList();
    }

    private static ActivityEntry ToEntry(DomainEvent e, VaultState state)
    {
        var (summary, orgId, categoryId) = Describe(e, state);
        return new ActivityEntry(e.EventId, e.EventType, e.OccurredAt, summary, orgId, categoryId);
    }

    private static (string Summary, string? OrgId, Guid? CategoryId) Describe(DomainEvent e, VaultState state) => e switch
    {
        VaultCreated v => ($"Vault created for {v.DisplayName}.", null, (Guid?)null),
        FieldUpdated f => ($"You updated {FieldName(state, f.FieldDefinitionId)}.", null, CategoryOfField(state, f.FieldDefinitionId)),
        CategoryCreated c => ($"You created category {c.Name}.", null, c.CategoryId),
        CategoryRenamed cr => ($"You renamed a category to {cr.NewName}.", null, cr.CategoryId),
        CategoryDeleted cd => ("You deleted a category.", null, cd.CategoryId),
        FieldDefinitionCreated fc => ($"You added field {fc.Name}.", null, fc.CategoryId),
        FieldDefinitionUpdated fu => ($"You renamed a field to {fu.NewName}.", null, CategoryOfField(state, fu.FieldDefinitionId)),
        FieldDefinitionDeleted fd => ("You deleted a field.", null, CategoryOfField(state, fd.FieldDefinitionId)),
        AgreementPresented a => ($"{a.OrganisationId} presented a data processing agreement.", a.OrganisationId, null),
        AgreementSigned a => ($"You accepted the agreement from {a.OrganisationId}.", a.OrganisationId, null),
        CategoryShared c => ($"You shared {CategoryName(state, c.CategoryId)} with {c.OrganisationId}.", c.OrganisationId, c.CategoryId),
        DataAccessed d => ($"{d.OrganisationId} accessed {CategoryName(state, d.CategoryId)} ({d.FieldsRead.Count} field(s)).", d.OrganisationId, d.CategoryId),
        ShareRevoked r => ($"You revoked {CategoryName(state, r.CategoryId)} access for {r.OrganisationId}.", r.OrganisationId, r.CategoryId),
        ShareExpired x => ($"Sharing of {CategoryName(state, x.CategoryId)} with {x.OrganisationId} expired.", x.OrganisationId, x.CategoryId),
        RenewalRequested rr => ($"{CategoryName(state, rr.CategoryId)} share with {rr.OrganisationId} expires in {rr.DaysRemaining} day(s).", rr.OrganisationId, rr.CategoryId),
        AccessDenied ad => ($"Denied access to {ad.OrganisationId}: {ad.Reason}", ad.OrganisationId, ad.CategoryId),
        PropagationSent p => ($"Change to {FieldName(state, p.ChangedFieldDefinitionId)} propagated to {p.NotifiedOrganisationIds.Count} organisation(s).", null, p.CategoryId),
        ConsentRenewed cr => ($"You renewed {CategoryName(state, cr.CategoryId)} sharing with {cr.OrganisationId}.", cr.OrganisationId, cr.CategoryId),
        _ => (e.EventType, null, null)
    };

    private static string CategoryName(VaultState state, Guid categoryId) =>
        state.Categories.TryGetValue(categoryId, out var category) ? category.Name : categoryId.ToString();

    private static string FieldName(VaultState state, Guid fieldDefinitionId) =>
        state.FieldDefinitions.TryGetValue(fieldDefinitionId, out var field) ? field.Name : fieldDefinitionId.ToString();

    private static Guid? CategoryOfField(VaultState state, Guid fieldDefinitionId) =>
        state.FieldDefinitions.TryGetValue(fieldDefinitionId, out var field) ? field.CategoryId : null;
}

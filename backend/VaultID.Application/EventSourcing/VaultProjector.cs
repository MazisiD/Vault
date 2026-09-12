using VaultID.Domain;
using VaultID.Domain.Categories;
using VaultID.Domain.Events;
using VaultID.Domain.Models;

namespace VaultID.Application.EventSourcing;

/// <summary>
/// Rebuilds <see cref="VaultState"/> by folding the event stream (blueprint 4.3
/// "the system's state is reconstructed from the event log"). This is the
/// canonical place where each event type's effect on current state is defined.
/// <para>
/// Since the dynamic-categories spec, this also projects the category/field
/// schema itself (categories and field definitions), not just field values -
/// schema changes are auditable vault events like any other.
/// </para>
/// </summary>
public static class VaultProjector
{
    /// <summary>Replays events in order to produce the current vault state.</summary>
    public static VaultState Project(string userId, IEnumerable<DomainEvent> events)
    {
        var state = new VaultState { UserId = userId };
        var version = 0L;

        foreach (var @event in events)
        {
            Apply(state, @event);
            version++;
        }

        state.Version = version;
        return state;
    }

    private static void Apply(VaultState state, DomainEvent @event)
    {
        switch (@event)
        {
            case VaultCreated e:
                state.Exists = true;
                state.DisplayName = e.DisplayName;
                foreach (var category in e.Categories)
                {
                    state.Categories[category.Id] = category;
                    state.Values.TryAdd(category.Id, new Dictionary<Guid, string?>());
                }

                foreach (var field in e.Fields)
                {
                    state.FieldDefinitions[field.Id] = field;
                }

                break;

            case CategoryCreated e:
                state.Categories[e.CategoryId] = new Category
                {
                    Id = e.CategoryId,
                    Name = e.Name,
                    IsSystem = e.IsSystem
                };
                state.Values.TryAdd(e.CategoryId, new Dictionary<Guid, string?>());
                break;

            case CategoryRenamed e when state.Categories.TryGetValue(e.CategoryId, out var renamedCategory):
                renamedCategory.Name = e.NewName;
                break;

            case CategoryDeleted e:
                state.Categories.Remove(e.CategoryId);
                state.Values.Remove(e.CategoryId);
                break;

            case FieldDefinitionCreated e:
                state.FieldDefinitions[e.FieldDefinitionId] = new FieldDefinition
                {
                    Id = e.FieldDefinitionId,
                    CategoryId = e.CategoryId,
                    ParentFieldDefinitionId = e.ParentFieldDefinitionId,
                    Name = e.Name,
                    FieldType = e.FieldType,
                    AutocompleteToken = e.AutocompleteToken,
                    Choices = e.Choices,
                    SortOrder = e.SortOrder
                };
                break;

            case FieldDefinitionUpdated e when state.FieldDefinitions.TryGetValue(e.FieldDefinitionId, out var updatedField):
                if (e.NewName is not null)
                {
                    updatedField.Name = e.NewName;
                }

                if (e.NewFieldType is { } newFieldType)
                {
                    updatedField.FieldType = newFieldType;
                }

                break;

            case FieldDefinitionDeleted e:
                if (state.FieldDefinitions.TryGetValue(e.FieldDefinitionId, out var deletedField))
                {
                    state.Values.TryGetValue(deletedField.CategoryId, out var owningBucket);
                    owningBucket?.Remove(e.FieldDefinitionId);
                }

                state.FieldDefinitions.Remove(e.FieldDefinitionId);
                break;

            case FieldUpdated e when state.FieldDefinitions.TryGetValue(e.FieldDefinitionId, out var field):
                var bucket = state.Values.TryGetValue(field.CategoryId, out var existing)
                    ? existing
                    : state.Values[field.CategoryId] = new Dictionary<Guid, string?>();
                bucket[e.FieldDefinitionId] = e.NewValue;
                break;

            case AgreementSigned e:
                state.SignedAgreements[e.OrganisationId] = e.AgreementId;
                break;

            case CategoryShared e:
                state.Grants[e.GrantId] = new PermissionGrant
                {
                    Id = e.GrantId,
                    GrantorUserId = state.UserId,
                    GranteeOrganisationId = e.OrganisationId,
                    CategoryId = e.CategoryId,
                    Scope = e.Scope,
                    Duration = e.Duration,
                    FieldDefinitionIds = e.FieldDefinitionIds,
                    AgreementId = e.AgreementId,
                    ExpiresAt = e.ExpiresAt,
                    Status = GrantStatus.Active,
                    ConsentedAt = e.OccurredAt,
                    UpdatedAt = e.OccurredAt
                };
                break;

            case ShareRevoked e when state.Grants.TryGetValue(e.GrantId, out var revoked):
                revoked.Status = GrantStatus.Revoked;
                revoked.UpdatedAt = e.OccurredAt;
                break;

            case ShareExpired e when state.Grants.TryGetValue(e.GrantId, out var expired):
                expired.Status = GrantStatus.Expired;
                expired.UpdatedAt = e.OccurredAt;
                break;

            case RenewalRequested e when state.Grants.TryGetValue(e.GrantId, out var pending):
                pending.Status = GrantStatus.PendingRenewal;
                pending.UpdatedAt = e.OccurredAt;
                break;

            case ConsentRenewed e when state.Grants.TryGetValue(e.GrantId, out var renewed):
                renewed.Status = GrantStatus.Active;
                renewed.ExpiresAt = e.NewExpiresAt;
                renewed.UpdatedAt = e.OccurredAt;
                break;

            case ShareExpiryChanged e when state.Grants.TryGetValue(e.GrantId, out var rescheduled):
                // Moving the expiry into the future revives a grant that had
                // already lapsed; a revoked grant is never revived this way
                // (the service refuses to emit the event for one).
                rescheduled.Status = GrantStatus.Active;
                rescheduled.ExpiresAt = e.NewExpiresAt;
                rescheduled.UpdatedAt = e.OccurredAt;
                break;

            case ShareCodeGenerated e:
                state.ShareCodes[e.ShareCodeId] = new ShareCode
                {
                    Id = e.ShareCodeId,
                    CodeHash = e.CodeHash,
                    OrganisationId = e.OrganisationId,
                    FieldDefinitionIds = e.FieldDefinitionIds,
                    AccessExpiresAt = e.AccessExpiresAt,
                    CodeExpiresAt = e.CodeExpiresAt,
                    Status = ShareCodeStatus.Pending,
                    CreatedAt = e.OccurredAt,
                    UpdatedAt = e.OccurredAt
                };
                break;

            case ShareCodeRedeemed e when state.ShareCodes.TryGetValue(e.ShareCodeId, out var redeemed):
                redeemed.Status = ShareCodeStatus.AwaitingApproval;
                redeemed.RedeemedAt = e.OccurredAt;
                redeemed.UpdatedAt = e.OccurredAt;
                break;

            case ShareCodeApproved e when state.ShareCodes.TryGetValue(e.ShareCodeId, out var approved):
                approved.Status = ShareCodeStatus.Approved;
                approved.GrantIds = e.GrantIds;
                approved.UpdatedAt = e.OccurredAt;
                break;

            case ShareCodeRejected e when state.ShareCodes.TryGetValue(e.ShareCodeId, out var rejected):
                rejected.Status = ShareCodeStatus.Rejected;
                rejected.UpdatedAt = e.OccurredAt;
                break;

            case ShareCodeRevoked e when state.ShareCodes.TryGetValue(e.ShareCodeId, out var cancelled):
                cancelled.Status = ShareCodeStatus.Revoked;
                cancelled.UpdatedAt = e.OccurredAt;
                break;

            case ShareCodeExpired e when state.ShareCodes.TryGetValue(e.ShareCodeId, out var lapsed):
                lapsed.Status = ShareCodeStatus.Expired;
                lapsed.UpdatedAt = e.OccurredAt;
                break;

            // AgreementPresented, DataAccessed, AccessDenied, PropagationSent are
            // audit-only and do not mutate vault state.
            default:
                break;
        }
    }
}

using VaultID.Domain;
using VaultID.Domain.Models;

namespace VaultID.Application.Permissions;

/// <summary>
/// The permission engine - the gatekeeper of the platform (blueprint 4.4).
/// Every organisation API request is evaluated here against the user's active
/// grants. It is pure decision logic over already-loaded state: it does not
/// perform I/O and does not record events (the calling service records the
/// resulting DataAccessed / AccessDenied audit event).
/// </summary>
public sealed class PermissionEngine
{
    /// <summary>
    /// Evaluates whether <paramref name="organisationId"/> may read
    /// <paramref name="categoryId"/> of the given vault right now, optionally
    /// narrowed to a single field.
    /// Checks, in order: vault exists, a grant exists, it is not revoked, and it
    /// is not expired (blueprint Figure 3).
    /// <para>
    /// Since the share-code flow, one organisation can hold several grants over
    /// the same category - each covering a different set of fields, from a
    /// different consent. So rather than inspecting one arbitrary grant, this
    /// looks for any grant that currently permits the request, and only falls
    /// back to explaining a denial when none does.
    /// </para>
    /// </summary>
    public PermissionDecision Evaluate(
        VaultState vault,
        string organisationId,
        Guid categoryId,
        DateTimeOffset now,
        Guid? fieldDefinitionId = null)
    {
        if (!vault.Exists)
        {
            return PermissionDecision.Deny("Vault does not exist.");
        }

        var candidates = vault.Grants.Values
            .Where(g => g.GranteeOrganisationId == organisationId && g.CategoryId == categoryId)
            .ToList();

        if (candidates.Count == 0)
        {
            return PermissionDecision.Deny("No permission grant exists for this organisation and category.");
        }

        var covering = fieldDefinitionId is { } fieldId
            ? candidates.Where(g => g.CoversField(fieldId)).ToList()
            : candidates;

        if (covering.Count == 0)
        {
            return PermissionDecision.Deny("This field is not part of what the user shared.");
        }

        // PendingRenewal still permits access until the hard expiry passes,
        // which IsCurrentlyActive does not allow for - so it is checked here.
        var live = covering.FirstOrDefault(g =>
            g.Status is GrantStatus.Active or GrantStatus.PendingRenewal &&
            (g.ExpiresAt is null || g.ExpiresAt > now));

        if (live is not null)
        {
            return PermissionDecision.Allow(live);
        }

        return covering.All(g => g.Status is GrantStatus.Revoked)
            ? PermissionDecision.Deny("Access was revoked by the user.")
            : PermissionDecision.Deny("The sharing period has expired.");
    }
}

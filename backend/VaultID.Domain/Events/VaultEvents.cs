using VaultID.Domain.Categories;

namespace VaultID.Domain.Events;

// =============================================================================
// The complete set of event types from the blueprint (4.3), extended by the
// dynamic-categories spec with schema-mutation events. Each records the data
// described there. Old field values are hashed - never stored in clear - to
// preserve the audit trail without leaking historical personal data.
// =============================================================================

/// <summary>
/// User signs up; an empty vault is created and seeded with the 3 system
/// categories and their field definitions (dynamic-categories spec,
/// "Migration / seeding" - previously via <c>CategoryCatalog.DefaultCategories</c>).
/// </summary>
public sealed record VaultCreated : DomainEvent
{
    public required string DisplayName { get; init; }
    public required IReadOnlyList<Category> Categories { get; init; }
    public required IReadOnlyList<FieldDefinition> Fields { get; init; }
}

/// <summary>User edits a field. The old value is stored hashed, never in clear.</summary>
public sealed record FieldUpdated : DomainEvent
{
    public required Guid FieldDefinitionId { get; init; }
    public required string NewValue { get; init; }
    public string? OldValueHash { get; init; }
}

/// <summary>User creates a custom category on their own vault.</summary>
public sealed record CategoryCreated : DomainEvent
{
    public required Guid CategoryId { get; init; }
    public required string Name { get; init; }
    public required bool IsSystem { get; init; }
}

/// <summary>User renames a (non-system) category.</summary>
public sealed record CategoryRenamed : DomainEvent
{
    public required Guid CategoryId { get; init; }
    public required string NewName { get; init; }
}

/// <summary>User deletes a (non-system, empty) category.</summary>
public sealed record CategoryDeleted : DomainEvent
{
    public required Guid CategoryId { get; init; }
}

/// <summary>User adds a field (or container) to one of their categories.</summary>
public sealed record FieldDefinitionCreated : DomainEvent
{
    public required Guid FieldDefinitionId { get; init; }
    public required Guid CategoryId { get; init; }
    public Guid? ParentFieldDefinitionId { get; init; }
    public required string Name { get; init; }
    public required FieldType FieldType { get; init; }
    public string? AutocompleteToken { get; init; }
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>Render this field's value masked until the owner reveals it.</summary>
    public bool IsSecret { get; init; }

    /// <summary>Singular noun for one item, on a Collection field.</summary>
    public string? ItemNoun { get; init; }

    /// <summary>This child's value names an item of its owning Collection.</summary>
    public bool IsItemTitle { get; init; }

    public required int SortOrder { get; init; }
}

/// <summary>
/// User changes a field definition's schema: a rename (<see cref="NewName"/>),
/// a type change (<see cref="NewFieldType"/> - emitted when a scalar field is
/// promoted to a container because a sub-field was added under it), a change to
/// whether the value is masked (<see cref="NewIsSecret"/>), or a change to a
/// Collection's item noun (<see cref="NewItemNoun"/>). A null property means
/// "unchanged".
/// </summary>
public sealed record FieldDefinitionUpdated : DomainEvent
{
    public required Guid FieldDefinitionId { get; init; }
    public string? NewName { get; init; }
    public FieldType? NewFieldType { get; init; }
    public IReadOnlyList<string>? NewChoices { get; init; }
    public bool? NewIsSecret { get; init; }
    public string? NewItemNoun { get; init; }
}

/// <summary>
/// User deletes a field definition (or one child of a cascading container
/// delete - one event per deleted definition).
/// </summary>
public sealed record FieldDefinitionDeleted : DomainEvent
{
    public required Guid FieldDefinitionId { get; init; }
}

// =============================================================================
// Collection items: a Collection field's children are a template, and the user
// adds as many items as they need. An item is identified by its own id and
// holds one value per child definition.
// =============================================================================

/// <summary>User adds one item to a Collection field (a bank account, a vehicle).</summary>
public sealed record CollectionItemAdded : DomainEvent
{
    public required Guid FieldDefinitionId { get; init; }
    public required Guid ItemId { get; init; }
    public required int SortOrder { get; init; }
}

/// <summary>User removes one item from a Collection field, and every value it held.</summary>
public sealed record CollectionItemRemoved : DomainEvent
{
    public required Guid FieldDefinitionId { get; init; }
    public required Guid ItemId { get; init; }
}

/// <summary>
/// User edits one field of one collection item. The old value is stored hashed,
/// never in clear - the same audit rule as <see cref="FieldUpdated"/>.
/// </summary>
public sealed record CollectionItemFieldUpdated : DomainEvent
{
    public required Guid ItemId { get; init; }
    public required Guid FieldDefinitionId { get; init; }
    public required string NewValue { get; init; }
    public string? OldValueHash { get; init; }
}

/// <summary>An organisation submits DPA terms to be shown to the user.</summary>
public sealed record AgreementPresented : DomainEvent
{
    public required string OrganisationId { get; init; }
    public required string AgreementId { get; init; }
    public required string Purpose { get; init; }
    public required int RetentionDays { get; init; }
    public required string LegalBasis { get; init; }
    public string? ThirdPartySharing { get; init; }
    public string? DeletionCommitment { get; init; }
}

/// <summary>User accepts an organisation's DPA. Signed agreement is immutable.</summary>
public sealed record AgreementSigned : DomainEvent
{
    public required string OrganisationId { get; init; }
    public required string AgreementId { get; init; }

    /// <summary>Hash of the exact agreement text the user consented to.</summary>
    public required string AgreementHash { get; init; }

    public required ConsentMethod ConsentMethod { get; init; }
}

/// <summary>User grants an organisation access to a category for a duration.</summary>
public sealed record CategoryShared : DomainEvent
{
    public required Guid GrantId { get; init; }
    public required string OrganisationId { get; init; }
    public required Guid CategoryId { get; init; }
    public required AccessScope Scope { get; init; }
    public required ShareDuration Duration { get; init; }
    public required string AgreementId { get; init; }

    /// <summary>
    /// The specific fields within the category the user ticked. Null means the
    /// whole category, which is how shares behaved before the share-code flow.
    /// </summary>
    public IReadOnlyList<Guid>? FieldDefinitionIds { get; init; }

    /// <summary>The share code this grant came from, when it came from one.</summary>
    public Guid? ShareCodeId { get; init; }

    /// <summary>Null for an indefinite share.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>An organisation successfully queried the API. Logged for transparency.</summary>
public sealed record DataAccessed : DomainEvent
{
    public required string OrganisationId { get; init; }
    public required Guid CategoryId { get; init; }
    public required IReadOnlyList<Guid> FieldsRead { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>User toggles sharing off. Access ends immediately.</summary>
public sealed record ShareRevoked : DomainEvent
{
    public required Guid GrantId { get; init; }
    public required string OrganisationId { get; init; }
    public required Guid CategoryId { get; init; }
}

/// <summary>A sharing duration timer ran out; access ends automatically.</summary>
public sealed record ShareExpired : DomainEvent
{
    public required Guid GrantId { get; init; }
    public required string OrganisationId { get; init; }
    public required Guid CategoryId { get; init; }
    public required ShareDuration OriginalDuration { get; init; }
}

/// <summary>Sharing is approaching expiry; user is prompted to renew or let it lapse.</summary>
public sealed record RenewalRequested : DomainEvent
{
    public required Guid GrantId { get; init; }
    public required string OrganisationId { get; init; }
    public required Guid CategoryId { get; init; }
    public required int DaysRemaining { get; init; }
}

/// <summary>An organisation queried without a valid permission. Logged for audit.</summary>
public sealed record AccessDenied : DomainEvent
{
    public required string OrganisationId { get; init; }
    public Guid? CategoryId { get; init; }
    public required IReadOnlyList<Guid> AttemptedFields { get; init; }
    public required string Reason { get; init; }
}

/// <summary>A field changed while shared; orgs with access were notified.</summary>
public sealed record PropagationSent : DomainEvent
{
    public required Guid CategoryId { get; init; }
    public required Guid ChangedFieldDefinitionId { get; init; }
    public required IReadOnlyList<string> NotifiedOrganisationIds { get; init; }
}

/// <summary>User re-confirms an existing share with a new duration/expiry.</summary>
public sealed record ConsentRenewed : DomainEvent
{
    public required Guid GrantId { get; init; }
    public required string OrganisationId { get; init; }
    public required Guid CategoryId { get; init; }
    public required ShareDuration NewDuration { get; init; }
    public DateTimeOffset? NewExpiresAt { get; init; }
}

/// <summary>
/// User moves an existing share's expiry to a different date and time - either
/// extending or shortening it. Distinct from <see cref="ConsentRenewed"/>,
/// which re-consents to a preset duration; this is the user directly steering
/// the exact end date of a share they already approved.
/// </summary>
public sealed record ShareExpiryChanged : DomainEvent
{
    public required Guid GrantId { get; init; }
    public required string OrganisationId { get; init; }
    public required Guid CategoryId { get; init; }
    public required DateTimeOffset NewExpiresAt { get; init; }
}

// =============================================================================
// Share codes: the user generates a code for a chosen organisation over a
// chosen set of fields, hands it over out-of-band, and the organisation
// redeems it to raise a request the user must then approve.
// =============================================================================

/// <summary>
/// User generates a share code for one organisation over a chosen set of
/// fields. Only the hash of the code is recorded - the plaintext is returned
/// to the user once and never stored.
/// </summary>
public sealed record ShareCodeGenerated : DomainEvent
{
    public required Guid ShareCodeId { get; init; }

    /// <summary>Hex-encoded SHA-256 of the normalised code.</summary>
    public required string CodeHash { get; init; }

    /// <summary>The only organisation permitted to redeem this code.</summary>
    public required string OrganisationId { get; init; }

    public required IReadOnlyList<Guid> FieldDefinitionIds { get; init; }

    /// <summary>The access expiry to apply to the grants created on approval.</summary>
    public required DateTimeOffset AccessExpiresAt { get; init; }

    /// <summary>When the unredeemed code stops being usable.</summary>
    public required DateTimeOffset CodeExpiresAt { get; init; }
}

/// <summary>
/// The target organisation presented a valid code. This raises a request for
/// the user to review - it grants no access on its own.
/// </summary>
public sealed record ShareCodeRedeemed : DomainEvent
{
    public required Guid ShareCodeId { get; init; }
    public required string OrganisationId { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>
/// User reviewed the organisation's agreement and approved the request. The
/// accompanying <see cref="CategoryShared"/> events carry the actual grants.
/// </summary>
public sealed record ShareCodeApproved : DomainEvent
{
    public required Guid ShareCodeId { get; init; }
    public required string OrganisationId { get; init; }
    public required IReadOnlyList<Guid> GrantIds { get; init; }
}

/// <summary>User declined the organisation's request. The code is spent.</summary>
public sealed record ShareCodeRejected : DomainEvent
{
    public required Guid ShareCodeId { get; init; }
    public required string OrganisationId { get; init; }
}

/// <summary>User cancelled a code before the organisation acted on it.</summary>
public sealed record ShareCodeRevoked : DomainEvent
{
    public required Guid ShareCodeId { get; init; }
    public required string OrganisationId { get; init; }
}

/// <summary>A code's validity window elapsed without the user deciding.</summary>
public sealed record ShareCodeExpired : DomainEvent
{
    public required Guid ShareCodeId { get; init; }
    public required string OrganisationId { get; init; }
}

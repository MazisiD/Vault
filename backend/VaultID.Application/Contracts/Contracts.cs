using VaultID.Domain;

namespace VaultID.Application.Contracts;

// =============================================================================
// Application boundary contracts. These records are the shape of the inputs and
// outputs of the use-case services. The Api layer maps HTTP <-> these; it never
// touches domain events or stored records directly.
// =============================================================================

public sealed record CreateVaultRequest(string UserId, string DisplayName);

public sealed record UpdateFieldRequest(Guid FieldDefinitionId, string Value);

/// <summary>One field's new value within a category-wide save.</summary>
public sealed record FieldValueUpdate(Guid FieldDefinitionId, string Value);

/// <summary>
/// One item of a Collection field as the user wants it to stand after the save.
/// A <paramref name="ItemId"/> the vault doesn't know yet is a newly added item.
/// </summary>
public sealed record CollectionItemUpdate(
    Guid ItemId,
    IReadOnlyList<FieldValueUpdate> Values);

/// <summary>
/// The complete desired contents of one Collection field. Items the vault
/// currently holds but that are absent from <paramref name="Items"/> are the
/// ones the user deleted, so the whole collection is saved as one picture
/// rather than as a stream of add/remove calls.
/// </summary>
public sealed record CollectionUpdate(
    Guid FieldDefinitionId,
    IReadOnlyList<CollectionItemUpdate> Items);

/// <summary>
/// Every edit the user made to one category, saved as a single change. The
/// whole set is validated before anything is written, so a category is never
/// left half-saved, and one event is still recorded per field that actually
/// changed - the audit trail stays field-level even though the user saves a
/// category at a time.
/// </summary>
public sealed record UpdateCategoryFieldsRequest(
    Guid CategoryId,
    IReadOnlyList<FieldValueUpdate> Values,
    IReadOnlyList<CollectionUpdate>? Collections = null);

/// <summary>One item of a Collection field, with the values it currently holds.</summary>
public sealed record CollectionItemView(
    Guid ItemId,
    IReadOnlyDictionary<Guid, string?> Fields);

/// <summary>
/// Current values of one category. <paramref name="Fields"/> holds the values of
/// ordinary fields and Group children, keyed by field definition id;
/// <paramref name="Collections"/> holds the items of each Collection field,
/// keyed by that collection's field definition id.
/// </summary>
public sealed record CategoryView(
    Guid CategoryId,
    IReadOnlyDictionary<Guid, string?> Fields,
    IReadOnlyDictionary<Guid, IReadOnlyList<CollectionItemView>> Collections);

/// <summary>A category the vault currently defines (for the vault summary).</summary>
public sealed record CategorySummary(Guid Id, string Name, bool IsSystem);

public sealed record VaultSummary(
    string UserId,
    string DisplayName,
    bool Exists,
    IReadOnlyList<CategorySummary> Categories,
    int ActiveShareCount);

public sealed record ShareRequest(
    string OrganisationId,
    Guid CategoryId,
    AccessScope Scope,
    ShareDuration Duration,
    ConsentMethod ConsentMethod);

public sealed record GrantView(
    Guid GrantId,
    string UserId,
    string UserDisplayName,
    string OrganisationId,
    string OrganisationName,
    Guid CategoryId,
    AccessScope Scope,
    ShareDuration Duration,
    string AgreementId,
    DateTimeOffset? ExpiresAt,
    GrantStatus Status,
    DateTimeOffset ConsentedAt,
    IReadOnlyList<Guid>? FieldDefinitionIds);

public sealed record RenewShareRequest(Guid GrantId, ShareDuration NewDuration);

/// <summary>Moves an existing share's end date, either forward or backward.</summary>
public sealed record ChangeExpiryRequest(Guid GrantId, DateTimeOffset NewExpiresAt);

// --- Share codes ---

/// <summary>
/// The user's request to mint a code for one organisation over a chosen set of
/// fields, granting access until <paramref name="AccessExpiresAt"/> once approved.
/// </summary>
public sealed record GenerateShareCodeRequest(
    string OrganisationId,
    IReadOnlyList<Guid> FieldDefinitionIds,
    DateTimeOffset AccessExpiresAt,
    AccessScope Scope,
    ConsentMethod ConsentMethod);

/// <summary>
/// Returned once, immediately after generation. <paramref name="Code"/> is the
/// only time the plaintext code is ever exposed - it is not recoverable
/// afterwards, since only its hash is stored.
/// </summary>
public sealed record GeneratedShareCodeView(
    Guid ShareCodeId,
    string Code,
    string OrganisationId,
    string OrganisationName,
    DateTimeOffset CodeExpiresAt,
    DateTimeOffset AccessExpiresAt);

/// <summary>One field a share code covers, with enough detail for the user to recognise it.</summary>
public sealed record SharedFieldView(
    Guid FieldDefinitionId,
    Guid CategoryId,
    string CategoryName,
    string FieldName);

/// <summary>A share code as listed back to its owner. Never carries the plaintext code.</summary>
public sealed record ShareCodeView(
    Guid ShareCodeId,
    string OrganisationId,
    string OrganisationName,
    ShareCodeStatus Status,
    IReadOnlyList<SharedFieldView> Fields,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset CodeExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RedeemedAt);

/// <summary>
/// A redeemed code awaiting the user's decision, bundled with the agreement the
/// user must read before approving.
/// </summary>
public sealed record PendingShareRequestView(
    Guid ShareCodeId,
    string OrganisationId,
    string OrganisationName,
    IReadOnlyList<SharedFieldView> Fields,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset RequestedAt,
    AgreementView Agreement);

/// <summary>
/// What the organisation learns by redeeming a code: which user it concerns and
/// that the request is now queued for that user's approval. No field values.
/// </summary>
public sealed record ShareCodeRedemptionView(
    Guid ShareCodeId,
    string UserId,
    ShareCodeStatus Status,
    int FieldCount,
    DateTimeOffset AccessExpiresAt);

/// <summary>
/// One share-code request an organisation has redeemed, with its current
/// status, for the organisation's own view of everything it has asked for.
/// </summary>
public sealed record OrganisationShareRequestView(
    Guid ShareCodeId,
    string UserId,
    string UserDisplayName,
    ShareCodeStatus Status,
    int FieldCount,
    DateTimeOffset RequestedAt,
    DateTimeOffset AccessExpiresAt);

/// <summary>
/// One line of the activity feed. <paramref name="EventType"/> is the machine
/// discriminator (used for icon/tone lookup and filtering);
/// <paramref name="EventLabel"/> is the readable form - "Field updated" rather
/// than "FieldUpdated" - and is what the UI displays.
/// </summary>
public sealed record ActivityEntry(
    Guid EventId,
    string EventType,
    string EventLabel,
    DateTimeOffset OccurredAt,
    string Summary,
    string? OrganisationId,
    Guid? CategoryId);

public sealed record RegisterOrganisationRequest(
    string Name,
    string Purpose,
    int RetentionDays,
    string LegalBasis,
    string? ThirdPartySharing,
    string? DeletionCommitment,
    string? RegistrationNumber = null,
    string? Address = null,
    string? Industry = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null);

public sealed record AgreementView(
    string OrganisationId,
    string OrganisationName,
    string AgreementId,
    string Purpose,
    int RetentionDays,
    string LegalBasis,
    string? ThirdPartySharing,
    string? DeletionCommitment);

/// <summary>Organisation profile fields collected during onboarding, editable later by an admin.</summary>
public sealed record OrganisationProfileView(
    string? RegistrationNumber,
    string? Address,
    string? Industry,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail);

public sealed record UpdateOrganisationProfileRequest(
    string Name,
    string? RegistrationNumber,
    string? Address,
    string? Industry,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail);

public sealed record UpdateOrganisationComplianceRequest(
    string Purpose,
    int RetentionDays,
    string LegalBasis,
    string? ThirdPartySharing,
    string? DeletionCommitment);

/// <summary>Sets (or replaces) one category's own agreement terms, overriding the org default.</summary>
public sealed record SetCategoryAgreementRequest(
    string CategoryName,
    string Purpose,
    int RetentionDays,
    string LegalBasis,
    string? ThirdPartySharing,
    string? DeletionCommitment);

/// <summary>A category's effective agreement, and whether it's a custom override or the org default.</summary>
public sealed record CategoryAgreementView(
    string CategoryName,
    bool HasOverride,
    AgreementView Agreement);

public sealed record AddPendingInvitesRequest(IReadOnlyList<string> Emails);

public sealed record OrganisationView(
    string Id,
    string Name,
    string Status,
    AgreementView? Agreement,
    OrganisationProfileView? Profile = null,
    IReadOnlyList<string>? PendingInvites = null);

public sealed record VerifyResult(bool Matches);

public sealed record WebhookSubscriptionView(
    string Id,
    string OrganisationId,
    string UserId,
    string CallbackUrl);

/// <summary>Returned by the org-facing data query so the Api can map denial to a 403.</summary>
public sealed record DataQueryResult(
    bool Allowed,
    string? DenialReason,
    Guid CategoryId,
    IReadOnlyDictionary<Guid, string?> Fields);

// --- Category/field schema (dynamic-categories spec) ---

public sealed record CreateCategoryRequest(string Name);

/// <summary>
/// Create one field (or container) in a category. <paramref name="FieldType"/>
/// is optional and defaults to <see cref="FieldType.Text"/>: the vault UI adds
/// a field by name alone, and a field becomes a Group implicitly when the first
/// sub-field is added under it.
/// </summary>
public sealed record CreateFieldRequest(
    string Name,
    FieldType? FieldType,
    string? AutocompleteToken,
    IReadOnlyList<string>? Choices,
    Guid? ParentFieldDefinitionId,
    bool IsSecret = false,
    string? ItemNoun = null);

/// <summary>One category's full nested schema, as returned by the metadata endpoint.</summary>
public sealed record CategorySchemaView(
    Guid Id,
    string Name,
    bool IsSystem,
    IReadOnlyList<FieldDefinitionView> Fields);

/// <summary>One field definition's schema, nested one level via <see cref="Children"/>.</summary>
public sealed record FieldDefinitionView(
    Guid Id,
    Guid CategoryId,
    Guid? ParentFieldDefinitionId,
    string Name,
    FieldType FieldType,
    string? AutocompleteToken,
    IReadOnlyList<string>? Choices,
    int SortOrder,
    IReadOnlyList<FieldDefinitionView> Children,
    bool IsSecret = false,
    string? ItemNoun = null,
    bool IsItemTitle = false);

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
/// Every edit the user made to one category, saved as a single change. The
/// whole set is validated before anything is written, so a category is never
/// left half-saved, and one <c>FieldUpdated</c> event is still recorded per
/// field that actually changed - the audit trail stays field-level even though
/// the user saves a category at a time.
/// </summary>
public sealed record UpdateCategoryFieldsRequest(
    Guid CategoryId,
    IReadOnlyList<FieldValueUpdate> Values);

/// <summary>Current values of one category, keyed by field definition id.</summary>
public sealed record CategoryView(
    Guid CategoryId,
    IReadOnlyDictionary<Guid, string?> Fields);

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
    string? DeletionCommitment);

public sealed record AgreementView(
    string OrganisationId,
    string OrganisationName,
    string AgreementId,
    string Purpose,
    int RetentionDays,
    string LegalBasis,
    string? ThirdPartySharing,
    string? DeletionCommitment);

public sealed record OrganisationView(
    string Id,
    string Name,
    string Status,
    AgreementView? Agreement);

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
/// Create one field (or Group) in a category. <paramref name="FieldType"/> is
/// optional and defaults to <see cref="FieldType.Text"/>: the vault UI adds a
/// field by name alone, and a field becomes a Group implicitly when the first
/// sub-field is added under it.
/// </summary>
public sealed record CreateFieldRequest(
    string Name,
    FieldType? FieldType,
    string? AutocompleteToken,
    IReadOnlyList<string>? Choices,
    Guid? ParentFieldDefinitionId);

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
    IReadOnlyList<FieldDefinitionView> Children);

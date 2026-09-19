using VaultID.Domain;

namespace VaultID.Api.Contracts;

// Transport DTOs for request bodies. Kept separate from Application contracts so
// the wire format can evolve independently of the use-case signatures.

public sealed record CreateVaultBody(string UserId, string DisplayName);

public sealed record UpdateFieldBody(string Value);

/// <summary>One field's new value inside a category-level save.</summary>
public sealed record FieldValueBody(Guid FieldDefinitionId, string Value);

/// <summary>One item of a collection, with the values it should hold after the save.</summary>
public sealed record CollectionItemBody(Guid ItemId, IReadOnlyList<FieldValueBody> Values);

/// <summary>
/// The complete set of items a collection should hold after the save. Items the
/// vault currently has but that are missing here are the ones the user deleted.
/// </summary>
public sealed record CollectionBody(Guid FieldDefinitionId, IReadOnlyList<CollectionItemBody> Items);

/// <summary>Every edit the user made to one category, saved as one change.</summary>
public sealed record UpdateCategoryFieldsBody(
    IReadOnlyList<FieldValueBody> Values,
    IReadOnlyList<CollectionBody>? Collections);

public sealed record ShareBody(
    string OrganisationId,
    Guid CategoryId,
    AccessScope Scope,
    ShareDuration Duration,
    ConsentMethod ConsentMethod);

public sealed record RenewBody(ShareDuration NewDuration);

/// <summary>Moves an existing share's end date to an exact instant.</summary>
public sealed record ChangeExpiryBody(DateTimeOffset NewExpiresAt);

// --- Share codes ---

/// <summary>
/// Mint a code for one organisation over a chosen set of fields, valid until
/// <paramref name="AccessExpiresAt"/> once the organisation's request is approved.
/// </summary>
public sealed record GenerateShareCodeBody(
    string OrganisationId,
    IReadOnlyList<Guid> FieldDefinitionIds,
    DateTimeOffset AccessExpiresAt,
    AccessScope? Scope,
    ConsentMethod? ConsentMethod);

/// <summary>The user's decision on a redeemed code.</summary>
public sealed record ApproveShareCodeBody(ConsentMethod? ConsentMethod);

/// <summary>An organisation presenting a share code for redemption.</summary>
public sealed record RedeemShareCodeBody(string Code);

public sealed record RegisterOrganisationBody(
    string Name,
    string Purpose,
    int RetentionDays,
    string LegalBasis,
    string? ThirdPartySharing,
    string? DeletionCommitment);

public sealed record VerifyBody(string Value);

public sealed record SubscribeWebhookBody(string UserId, string CallbackUrl);

public sealed record ForgotUsernameBody(string Email);

// --- Category/field schema (dynamic-categories spec) ---

public sealed record CreateCategoryBody(string Name);

public sealed record RenameCategoryBody(string Name);

public sealed record CreateFieldBody(
    string Name,
    FieldType? FieldType,
    string? AutocompleteToken,
    IReadOnlyList<string>? Choices,
    Guid? ParentFieldDefinitionId,
    bool IsSecret = false,
    string? ItemNoun = null);

public sealed record RenameFieldBody(string Name);

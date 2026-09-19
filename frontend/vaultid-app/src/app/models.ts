/**
 * TypeScript mirrors of the backend API contracts. These are plain data shapes
 * for rendering only - the frontend contains NO business logic.
 */

export type AccessScope = 'ReadOnly' | 'ReadWithVerification';
/**
 * 'Custom' means the user picked an exact end date rather than one of the
 * preset windows; the preset members remain for grants created before
 * share codes existed.
 */
export type ShareDuration = 'ThirtyDays' | 'NinetyDays' | 'OneYear' | 'Indefinite' | 'Custom';
export type ConsentMethod = 'InAppConfirmation' | 'Biometric';
export type GrantStatus = 'Active' | 'Revoked' | 'Expired' | 'PendingRenewal';

/** Lifecycle of a share code. Mirrors backend/VaultID.Domain/Enums.cs `ShareCodeStatus`. */
export type ShareCodeStatus =
  | 'Pending'
  | 'AwaitingApproval'
  | 'Approved'
  | 'Rejected'
  | 'Expired'
  | 'Revoked';

/**
 * Dynamic-categories spec: the fixed set of field shapes a category can
 * define. Mirrors backend/VaultID.Domain/Enums.cs `FieldType` - the backend
 * serializes it as a string (JsonStringEnumConverter, no naming policy) so
 * these values must match the C# member names exactly.
 */
export type FieldType =
  | 'Text'
  | 'LongText'
  | 'Number'
  | 'Date'
  | 'Time'
  | 'Link'
  | 'Boolean'
  | 'Choice'
  | 'File'
  | 'Attachment'
  | 'Group'
  | 'Collection';

/**
 * One field definition in a category's schema (dynamic-categories spec).
 * Mirrors `FieldDefinitionView` (backend/VaultID.Application/Contracts/Contracts.cs).
 * A field is nested one level inside a `Group` or `Collection` field via
 * `children` (backed by `parentFieldDefinitionId` server-side) - the spec
 * allows only one level of nesting. For a `Group` the children are the parts
 * of a single value, e.g. an address; for a `Collection` they are the template
 * every item repeats, e.g. one bank account.
 */
export interface FieldDefinition {
  id: string;
  categoryId: string;
  parentFieldDefinitionId?: string | null;
  name: string;
  fieldType: FieldType;
  autocompleteToken?: string | null;
  /** Only meaningful when fieldType is 'Choice'. */
  choices?: string[] | null;
  sortOrder: number;
  children: FieldDefinition[];
  /** The value is masked until the owner reveals it, e.g. an ID number. */
  isSecret?: boolean;
  /** Only on a 'Collection': what one of its items is called, e.g. 'bank account'. */
  itemNoun?: string | null;
  /** Only on a child of a 'Collection': its value titles the item in a collapsed list. */
  isItemTitle?: boolean;
}

/**
 * One category's full nested schema, as returned by
 * GET /api/vaults/{userId}/metadata/categories. Mirrors `CategorySchemaView`.
 */
export interface Category {
  id: string;
  name: string;
  isSystem: boolean;
  fields: FieldDefinition[];
}

/** A category as listed on the vault summary (no schema detail). Mirrors `CategorySummary`. */
export interface CategorySummary {
  id: string;
  name: string;
  isSystem: boolean;
}

export interface VaultSummary {
  userId: string;
  displayName: string;
  exists: boolean;
  categories: CategorySummary[];
  activeShareCount: number;
}

/** One item of a collection field, with the values it holds. Mirrors `CollectionItemView`. */
export interface CollectionItem {
  itemId: string;
  fields: Record<string, string | null>;
}

/**
 * Current field values for one category. Mirrors `CategoryView`. `fields` holds
 * plain and group values keyed by field-definition id; `collections` holds the
 * items of each collection field, keyed by that collection's id.
 */
export interface CategoryView {
  categoryId: string;
  fields: Record<string, string | null>;
  collections: Record<string, CollectionItem[]>;
}

/** One field's new value inside a category-level save. */
export interface FieldValueUpdate {
  fieldDefinitionId: string;
  value: string;
}

/** The complete set of items a collection should hold after the save. */
export interface CollectionUpdate {
  fieldDefinitionId: string;
  items: { itemId: string; values: FieldValueUpdate[] }[];
}

/** Everything the user changed in one category, saved as a single request. */
export interface UpdateCategoryFieldsRequest {
  values: FieldValueUpdate[];
  collections: CollectionUpdate[];
}

export interface Agreement {
  organisationId: string;
  organisationName: string;
  agreementId: string;
  purpose: string;
  retentionDays: number;
  legalBasis: string;
  thirdPartySharing?: string | null;
  deletionCommitment?: string | null;
}

export interface Organisation {
  id: string;
  name: string;
  status: string;
  agreement?: Agreement | null;
}

export interface Grant {
  grantId: string;
  userId: string;
  organisationId: string;
  organisationName: string;
  categoryId: string;
  scope: AccessScope;
  duration: ShareDuration;
  agreementId: string;
  expiresAt?: string | null;
  status: GrantStatus;
  consentedAt: string;
  /** Which fields of the category are covered. `null` means the whole category. */
  fieldDefinitionIds?: string[] | null;
}

/** One field named inside a share code, resolved for display. Mirrors `SharedFieldView`. */
export interface SharedField {
  fieldDefinitionId: string;
  categoryId: string;
  categoryName: string;
  fieldName: string;
}

/** A code the user minted, as listed on the Sharing screen. Mirrors `ShareCodeView`. */
export interface ShareCode {
  shareCodeId: string;
  organisationId: string;
  organisationName: string;
  status: ShareCodeStatus;
  fields: SharedField[];
  accessExpiresAt: string;
  codeExpiresAt: string;
  createdAt: string;
  redeemedAt?: string | null;
}

/**
 * The plaintext code, returned once at generation and never again - the
 * server only keeps a hash of it.
 */
export interface GeneratedShareCode {
  shareCodeId: string;
  code: string;
  organisationId: string;
  organisationName: string;
  codeExpiresAt: string;
  accessExpiresAt: string;
}

/** What an organisation learns by redeeming a share code: the user and queued request state. */
export interface ShareCodeRedemptionView {
  shareCodeId: string;
  userId: string;
  status: ShareCodeStatus;
  fieldCount: number;
  accessExpiresAt: string;
}

/** An organisation has redeemed a code and is waiting on the user's decision. */
export interface PendingShareRequest {
  shareCodeId: string;
  organisationId: string;
  organisationName: string;
  fields: SharedField[];
  accessExpiresAt: string;
  requestedAt: string;
  agreement: Agreement;
}

export interface GenerateShareCodeRequest {
  organisationId: string;
  fieldDefinitionIds: string[];
  /** ISO-8601 instant when the organisation's access should end. */
  accessExpiresAt: string;
  scope: AccessScope;
  consentMethod: ConsentMethod;
}

export interface ActivityEntry {
  eventId: string;
  /** Machine discriminator (e.g. `FieldUpdated`) - used for icon/tone lookup, never displayed. */
  eventType: string;
  /** Readable form of `eventType` (e.g. `Field updated`) - this is what the UI shows. */
  eventLabel: string;
  occurredAt: string;
  summary: string;
  organisationId?: string | null;
  categoryId?: string | null;
}

export interface ShareRequest {
  organisationId: string;
  categoryId: string;
  scope: AccessScope;
  duration: ShareDuration;
  consentMethod: ConsentMethod;
}

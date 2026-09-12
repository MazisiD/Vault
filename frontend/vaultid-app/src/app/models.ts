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
export type FieldType = 'Text' | 'LongText' | 'Number' | 'Date' | 'Boolean' | 'Choice' | 'File' | 'Group';

/**
 * One field definition in a category's schema (dynamic-categories spec).
 * Mirrors `FieldDefinitionView` (backend/VaultID.Application/Contracts/Contracts.cs).
 * A field is nested one level inside a `Group` field via `children` (backed
 * by `parentFieldDefinitionId` server-side) - the spec allows only one level
 * of nesting.
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

/** Current field values for one category, keyed by field-definition id. Mirrors `CategoryView`. */
export interface CategoryView {
  categoryId: string;
  fields: Record<string, string | null>;
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
  eventType: string;
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

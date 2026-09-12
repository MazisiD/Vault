import { GrantStatus } from './models';

/**
 * Display name for each field name defined in the backend's seed data
 * (backend/VaultID.Domain/Categories/CategoryCatalog.cs). Since the
 * dynamic-categories rewrite, fields are identified by a `Guid` that is
 * generated fresh per vault at creation time - it is never the same value
 * across two vaults, so it cannot be used as a static lookup key. The field
 * *name* is still stable (the seed data uses these exact names for every
 * vault), so this map is keyed by name, same as before; `deriveDisplayName`
 * below is the fallback for any field that isn't a seeded system field
 * (i.e. any custom field a user has created).
 */
const FIELD_DISPLAY_NAMES: Record<string, string> = {
  // Biographical
  FullName: 'Full name',
  IdOrPassportNumber: 'ID or passport number',
  DateOfBirth: 'Date of birth',
  Gender: 'Gender',
  PhysicalAddress: 'Physical address',
  PostalAddress: 'Postal address',
  PhoneNumber: 'Phone number',
  EmailAddress: 'Email address',
  Nationality: 'Nationality',
  HomeLanguage: 'Home language',
  MaritalStatus: 'Marital status',
  BankAccountDetails: 'Bank account details',
  TaxNumber: 'Tax number',
  NextOfKin: 'Next of kin',
  // Health
  BloodType: 'Blood type',
  KnownAllergies: 'Known allergies',
  ChronicConditions: 'Chronic conditions',
  CurrentMedications: 'Current medications',
  MedicalAidProvider: 'Medical aid provider',
  MedicalAidNumber: 'Medical aid number',
  VaccinationRecords: 'Vaccination records',
  DisabilityStatus: 'Disability status',
  EmergencyContact: 'Emergency contact',
  PrimaryDoctor: 'Primary doctor',
  MentalHealthNotes: 'Mental health notes',
  OrganDonorStatus: 'Organ donor status',
  // Educational
  HighestQualification: 'Highest qualification',
  InstitutionsAttended: 'Institutions attended',
  DegreesOrDiplomas: 'Degrees or diplomas',
  AcademicTranscripts: 'Academic transcripts',
  ProfessionalCertifications: 'Professional certifications',
  SkillsOrShortCourses: 'Skills or short courses',
  StudentNumber: 'Student number',
  ResearchPublications: 'Research publications',
  ProfessionalMemberships: 'Professional memberships',
  CpdRecords: 'CPD records',
};

/**
 * Displays a field by its *name* (`FieldMeta.key` is the field's schema
 * name, not its Guid id - the id is only used for wiring inputs/values, never
 * for display lookups). `displayName` is UI-only.
 */
export interface FieldMeta {
  key: string;
  displayName: string;
}

export function fieldMeta(name: string): FieldMeta {
  return { key: name, displayName: FIELD_DISPLAY_NAMES[name] ?? deriveDisplayName(name) };
}

/** Fallback for any field name not in the map above: splits PascalCase and sentence-cases it. */
export function deriveDisplayName(name: string): string {
  const spaced = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

/**
 * Visual identity per category (icon + color tokens), per
 * `VaultID New Design/VaultID.dc.html`. One quiet, consistent mark per
 * category, carried from the vault into every grant chip and activity line.
 * Keyed by category *name* for the same reason as `FIELD_DISPLAY_NAMES`
 * above - category ids are per-vault random Guids, not stable across vaults -
 * so a custom, user-created category falls back to a neutral look via
 * `categoryMeta()`.
 */
export interface CategoryMeta {
  label: string;
  icon: string;
  dot: string;
  bg: string;
  text: string;
}

export const CATEGORY_META: Record<string, CategoryMeta> = {
  Biographical: {
    label: 'Biographical',
    icon: 'ph-identification-card',
    dot: 'var(--vid-cat-bio)',
    bg: 'var(--vid-cat-bio-bg)',
    text: 'var(--vid-cat-bio-text)',
  },
  Health: {
    label: 'Health',
    icon: 'ph-heartbeat',
    dot: 'var(--vid-cat-health)',
    bg: 'var(--vid-cat-health-bg)',
    text: 'var(--vid-cat-health-text)',
  },
  Educational: {
    label: 'Educational',
    icon: 'ph-graduation-cap',
    dot: 'var(--vid-cat-edu)',
    bg: 'var(--vid-cat-edu-bg)',
    text: 'var(--vid-cat-edu-text)',
  },
};

/** Fallback visual identity for a category not in `CATEGORY_META` (i.e. a custom, user-created one). */
const DEFAULT_CATEGORY_META: Omit<CategoryMeta, 'label'> = {
  icon: 'ph-folder',
  dot: 'var(--color-neutral-300)',
  bg: 'color-mix(in srgb, var(--color-text) 8%, transparent)',
  text: 'var(--color-text)',
};

/** Looks up a category's visual identity by name, falling back to a neutral look for custom categories. */
export function categoryMeta(name: string): CategoryMeta {
  return CATEGORY_META[name] ?? { label: name, ...DEFAULT_CATEGORY_META };
}

/** icon + tone for each grant status, used on the Sharing screen. */
export interface GrantStatusMeta {
  label: string;
  dot: string;
  text: string;
}

export function grantStatusMeta(status: GrantStatus, expiresAt?: string | null): GrantStatusMeta {
  if (status === 'Revoked') {
    return { label: 'Revoked', dot: 'var(--vid-status-revoked)', text: 'var(--vid-status-revoked)' };
  }
  if (status === 'Expired') {
    return { label: 'Expired', dot: 'var(--vid-status-neutral)', text: 'var(--vid-status-neutral)' };
  }
  if (status === 'PendingRenewal') {
    return { label: 'Renewal pending', dot: 'var(--vid-status-expiring)', text: 'var(--vid-status-expiring)' };
  }
  const daysLeft = expiresAt ? daysUntil(expiresAt) : null;
  if (daysLeft !== null && daysLeft <= 7) {
    return {
      label: `Expires in ${daysLeft} day${daysLeft === 1 ? '' : 's'}`,
      dot: 'var(--vid-status-expiring)',
      text: 'var(--vid-status-expiring)',
    };
  }
  return { label: 'Active', dot: 'var(--vid-status-ok)', text: 'var(--color-text)' };
}

function daysUntil(iso: string): number {
  const ms = new Date(iso).getTime() - Date.now();
  return Math.max(0, Math.ceil(ms / (1000 * 60 * 60 * 24)));
}

/** icon + tone for each activity event type emitted by the backend event stream. */
export interface EventMeta {
  icon: string;
  tone: 'ok' | 'neutral' | 'warn' | 'danger';
}

const EVENT_META: Record<string, EventMeta> = {
  VaultCreated: { icon: 'ph-vault', tone: 'neutral' },
  FieldUpdated: { icon: 'ph-pencil-simple', tone: 'neutral' },
  AgreementPresented: { icon: 'ph-file-text', tone: 'neutral' },
  AgreementSigned: { icon: 'ph-check-circle', tone: 'ok' },
  CategoryShared: { icon: 'ph-share-network', tone: 'ok' },
  DataAccessed: { icon: 'ph-eye', tone: 'neutral' },
  ShareRevoked: { icon: 'ph-prohibit', tone: 'danger' },
  ShareExpired: { icon: 'ph-clock-countdown', tone: 'warn' },
  RenewalRequested: { icon: 'ph-bell', tone: 'warn' },
  AccessDenied: { icon: 'ph-warning-circle', tone: 'danger' },
  PropagationSent: { icon: 'ph-broadcast', tone: 'neutral' },
  ConsentRenewed: { icon: 'ph-arrow-clockwise', tone: 'ok' },
};

const TONE_COLOR: Record<EventMeta['tone'], string> = {
  ok: 'var(--vid-status-ok)',
  neutral: 'var(--color-neutral-300)',
  warn: 'var(--vid-status-expiring)',
  danger: 'var(--vid-status-revoked)',
};

export function eventMeta(eventType: string): EventMeta & { toneColor: string } {
  const meta = EVENT_META[eventType] ?? { icon: 'ph-dot', tone: 'neutral' as const };
  return { ...meta, toneColor: TONE_COLOR[meta.tone] };
}

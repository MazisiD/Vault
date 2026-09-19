import { Injectable, signal } from '@angular/core';

/** Step 2's collected fields, held until step 3 submits registration with them. */
export interface OrgProfileDraft {
  purpose: string;
  registrationNumber: string | null;
  address: string | null;
  industry: string | null;
  contactName: string | null;
  contactPhone: string | null;
  contactEmail: string | null;
}

export interface OrgRegistrationStep {
  key: 'admin-account' | 'profile' | 'compliance' | 'category-agreements' | 'invite-team';
  label: string;
}

export const ORG_REGISTRATION_STEPS: OrgRegistrationStep[] = [
  { key: 'admin-account', label: 'Admin account' },
  { key: 'profile', label: 'Organisation profile' },
  { key: 'compliance', label: 'Data handling & compliance' },
  { key: 'category-agreements', label: 'Category agreements' },
  { key: 'invite-team', label: 'Invite team' },
];

/**
 * Holds the in-progress organisation registration wizard's state across its
 * steps, in memory only - the wizard is one continuous session, not a
 * resumable draft. The organisation itself is created partway through (once
 * the required profile + compliance steps are done); the remaining steps
 * edit that already-created organisation directly via the API, the same way
 * an admin would later from the organisation-admin screen.
 */
@Injectable({ providedIn: 'root' })
export class OrganisationRegistrationStateService {
  readonly stepIndex = signal(0);

  readonly adminUserId = signal<string | null>(null);
  readonly adminEmail = signal('');

  readonly organisationId = signal<string | null>(null);
  readonly organisationName = signal('');
  readonly profileDraft = signal<OrgProfileDraft | null>(null);

  get steps(): OrgRegistrationStep[] {
    return ORG_REGISTRATION_STEPS;
  }

  goToStep(index: number): void {
    if (index >= 0 && index < ORG_REGISTRATION_STEPS.length) {
      this.stepIndex.set(index);
    }
  }

  next(): void {
    this.goToStep(this.stepIndex() + 1);
  }

  reset(): void {
    this.stepIndex.set(0);
    this.adminUserId.set(null);
    this.adminEmail.set('');
    this.organisationId.set(null);
    this.organisationName.set('');
    this.profileDraft.set(null);
  }
}

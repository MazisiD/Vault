import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { VaultApiService } from '../../services/vault-api.service';
import { CategoryAgreementView, Organisation } from '../../models';

interface CategoryAgreementRow {
  categoryName: string;
  hasOverride: boolean;
  expanded: boolean;
  purpose: string;
  retentionDays: number;
  legalBasis: string;
  thirdPartySharing: string;
  deletionCommitment: string;
}

/**
 * Organisation admin settings: everything collected during registration
 * (profile, compliance/default agreement, per-category agreements, pending
 * invites), editable at any time by the organisation's admin.
 */
@Component({
  selector: 'app-organisation-admin',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './organisation-admin.component.html',
  styleUrl: './organisation-admin.component.css',
})
export class OrganisationAdminComponent implements OnInit {
  private readonly api = inject(VaultApiService);
  private readonly auth = inject(AuthService);

  readonly organisationId = signal<string | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly message = signal('');

  // Profile form
  name = '';
  registrationNumber = '';
  address = '';
  industry = '';
  contactName = '';
  contactPhone = '';
  contactEmail = '';
  readonly savingProfile = signal(false);

  // Compliance form
  purpose = '';
  retentionDays = 365;
  legalBasis = '';
  thirdPartySharing = '';
  deletionCommitment = '';
  readonly savingCompliance = signal(false);

  // Category agreements
  readonly categoryRows = signal<CategoryAgreementRow[]>([]);
  readonly savingCategory = signal<string | null>(null);

  // Invites
  pendingInvites: string[] = [];
  newInviteEmails = '';
  readonly savingInvites = signal(false);

  ngOnInit(): void {
    const organisationId = this.auth.organisationId;
    this.organisationId.set(organisationId);
    if (!organisationId) {
      this.loading.set(false);
      return;
    }

    Promise.all([
      firstValueFrom(this.api.getOrganisation(organisationId)),
      firstValueFrom(this.api.getCategoryCatalog()),
      firstValueFrom(this.api.listCategoryAgreements(organisationId)),
    ])
      .then(([org, categoryNames, overrides]) => {
        this.applyOrganisation(org);
        this.applyCategoryRows(categoryNames, overrides);
      })
      .catch(() => this.error.set('Could not load your organisation.'))
      .finally(() => this.loading.set(false));
  }

  private applyOrganisation(org: Organisation): void {
    this.name = org.name;
    this.registrationNumber = org.profile?.registrationNumber ?? '';
    this.address = org.profile?.address ?? '';
    this.industry = org.profile?.industry ?? '';
    this.contactName = org.profile?.contactName ?? '';
    this.contactPhone = org.profile?.contactPhone ?? '';
    this.contactEmail = org.profile?.contactEmail ?? '';

    this.purpose = org.agreement?.purpose ?? '';
    this.retentionDays = org.agreement?.retentionDays ?? 365;
    this.legalBasis = org.agreement?.legalBasis ?? '';
    this.thirdPartySharing = org.agreement?.thirdPartySharing ?? '';
    this.deletionCommitment = org.agreement?.deletionCommitment ?? '';

    this.pendingInvites = org.pendingInvites ?? [];
  }

  private applyCategoryRows(categoryNames: string[], overrides: CategoryAgreementView[]): void {
    const byName = new Map(overrides.map((o) => [o.categoryName, o]));
    this.categoryRows.set(categoryNames.map((categoryName) => {
      const existing = byName.get(categoryName);
      const agreement = existing?.agreement;
      return {
        categoryName,
        hasOverride: existing?.hasOverride ?? false,
        expanded: false,
        purpose: existing?.hasOverride ? agreement?.purpose ?? '' : '',
        retentionDays: existing?.hasOverride ? agreement?.retentionDays ?? 365 : 365,
        legalBasis: existing?.hasOverride ? agreement?.legalBasis ?? '' : '',
        thirdPartySharing: existing?.hasOverride ? agreement?.thirdPartySharing ?? '' : '',
        deletionCommitment: existing?.hasOverride ? agreement?.deletionCommitment ?? '' : '',
      };
    }));
  }

  toggleCategory(row: CategoryAgreementRow): void {
    row.expanded = !row.expanded;
  }

  async saveProfile(): Promise<void> {
    const organisationId = this.organisationId();
    if (!organisationId) return;

    this.error.set('');
    this.message.set('');
    this.savingProfile.set(true);
    try {
      await firstValueFrom(this.api.updateOrganisationProfile(organisationId, {
        name: this.name.trim(),
        registrationNumber: this.registrationNumber.trim() || null,
        address: this.address.trim() || null,
        industry: this.industry.trim() || null,
        contactName: this.contactName.trim() || null,
        contactPhone: this.contactPhone.trim() || null,
        contactEmail: this.contactEmail.trim() || null,
      }));
      this.message.set('Profile updated.');
    } catch {
      this.error.set('Could not update the profile.');
    } finally {
      this.savingProfile.set(false);
    }
  }

  async saveCompliance(): Promise<void> {
    const organisationId = this.organisationId();
    if (!organisationId) return;

    this.error.set('');
    this.message.set('');
    this.savingCompliance.set(true);
    try {
      await firstValueFrom(this.api.updateOrganisationCompliance(organisationId, {
        purpose: this.purpose.trim(),
        retentionDays: this.retentionDays,
        legalBasis: this.legalBasis.trim(),
        thirdPartySharing: this.thirdPartySharing.trim() || null,
        deletionCommitment: this.deletionCommitment.trim() || null,
      }));
      this.message.set('Default agreement updated.');
    } catch {
      this.error.set('Could not update the default agreement.');
    } finally {
      this.savingCompliance.set(false);
    }
  }

  async saveCategory(row: CategoryAgreementRow): Promise<void> {
    const organisationId = this.organisationId();
    if (!organisationId) return;

    this.error.set('');
    this.savingCategory.set(row.categoryName);
    try {
      await firstValueFrom(this.api.setCategoryAgreement(organisationId, {
        categoryName: row.categoryName,
        purpose: row.purpose.trim(),
        retentionDays: row.retentionDays,
        legalBasis: row.legalBasis.trim(),
        thirdPartySharing: row.thirdPartySharing.trim() || null,
        deletionCommitment: row.deletionCommitment.trim() || null,
      }));
      row.hasOverride = true;
      row.expanded = false;
    } catch {
      this.error.set(`Could not save the agreement for '${row.categoryName}'.`);
    } finally {
      this.savingCategory.set(null);
    }
  }

  async clearCategory(row: CategoryAgreementRow): Promise<void> {
    const organisationId = this.organisationId();
    if (!organisationId) return;

    this.savingCategory.set(row.categoryName);
    try {
      await firstValueFrom(this.api.clearCategoryAgreement(organisationId, row.categoryName));
      row.hasOverride = false;
      row.purpose = '';
      row.legalBasis = '';
      row.thirdPartySharing = '';
      row.deletionCommitment = '';
      row.retentionDays = 365;
    } catch {
      this.error.set(`Could not clear the agreement for '${row.categoryName}'.`);
    } finally {
      this.savingCategory.set(null);
    }
  }

  async addInvites(): Promise<void> {
    const organisationId = this.organisationId();
    const emails = this.newInviteEmails
      .split(/[,\n]/)
      .map((e) => e.trim())
      .filter((e) => e.length > 0);
    if (!organisationId || emails.length === 0) return;

    this.savingInvites.set(true);
    try {
      this.pendingInvites = await firstValueFrom(this.api.addPendingInvites(organisationId, emails));
      this.newInviteEmails = '';
    } catch {
      this.error.set('Could not save the invites.');
    } finally {
      this.savingInvites.set(false);
    }
  }
}

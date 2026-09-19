import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../services/auth.service';
import { VaultApiService } from '../../../services/vault-api.service';
import { OrganisationRegistrationStateService } from '../organisation-registration-state.service';

/**
 * Step 3: data handling & compliance terms. Required - submitting this step
 * is what actually registers the organisation with the backend registry
 * (combining it with step 2's profile fields), so by the time this step is
 * done the org exists and is already searchable for sharing.
 */
@Component({
  selector: 'app-compliance-step',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './compliance-step.component.html',
  styleUrl: './compliance-step.component.css',
})
export class ComplianceStepComponent {
  private readonly api = inject(VaultApiService);
  private readonly auth = inject(AuthService);
  private readonly state = inject(OrganisationRegistrationStateService);

  retentionDays = 365;
  legalBasis = '';
  thirdPartySharing = '';
  deletionCommitment = '';

  readonly submitting = signal(false);
  readonly error = signal('');

  async submit(): Promise<void> {
    this.error.set('');
    const profile = this.state.profileDraft();
    if (!profile) {
      this.error.set('Organisation profile is missing - go back and complete the previous step.');
      return;
    }

    this.submitting.set(true);
    try {
      const org = await firstValueFrom(this.api.registerOrganisation({
        name: this.state.organisationName(),
        purpose: profile.purpose,
        retentionDays: this.retentionDays,
        legalBasis: this.legalBasis.trim(),
        thirdPartySharing: this.thirdPartySharing.trim() || null,
        deletionCommitment: this.deletionCommitment.trim() || null,
        registrationNumber: profile.registrationNumber,
        address: profile.address,
        industry: profile.industry,
        contactName: profile.contactName,
        contactPhone: profile.contactPhone,
        contactEmail: profile.contactEmail,
      }));

      this.state.organisationId.set(org.id);
      await this.auth.linkOrganisation(org.id);
      this.state.next();
    } catch {
      this.error.set('Could not register the organisation. Please check the details and try again.');
    } finally {
      this.submitting.set(false);
    }
  }
}

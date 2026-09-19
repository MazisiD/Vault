import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OrganisationRegistrationStateService } from '../organisation-registration-state.service';

/** Step 2: organisation profile fields. Kept in wizard state; submitted together with compliance in step 3. */
@Component({
  selector: 'app-org-profile-step',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './org-profile-step.component.html',
  styleUrl: './org-profile-step.component.css',
})
export class OrgProfileStepComponent {
  readonly state = inject(OrganisationRegistrationStateService);

  name = this.state.organisationName();
  purpose = '';
  registrationNumber = '';
  address = '';
  industry = '';
  contactName = '';
  contactPhone = '';
  contactEmail = '';

  continue(): void {
    this.state.organisationName.set(this.name.trim());
    this.state.profileDraft.set({
      purpose: this.purpose.trim(),
      registrationNumber: this.registrationNumber.trim() || null,
      address: this.address.trim() || null,
      industry: this.industry.trim() || null,
      contactName: this.contactName.trim() || null,
      contactPhone: this.contactPhone.trim() || null,
      contactEmail: this.contactEmail.trim() || null,
    });
    this.state.next();
  }
}

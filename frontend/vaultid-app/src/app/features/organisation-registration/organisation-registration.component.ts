import { Component } from '@angular/core';
import { AdminAccountStepComponent } from './admin-account-step/admin-account-step.component';
import { OrgProfileStepComponent } from './org-profile-step/org-profile-step.component';
import { ComplianceStepComponent } from './compliance-step/compliance-step.component';
import { CategoryAgreementsStepComponent } from './category-agreements-step/category-agreements-step.component';
import { InviteTeamStepComponent } from './invite-team-step/invite-team-step.component';
import { OrganisationRegistrationStateService } from './organisation-registration-state.service';

/**
 * Multi-step organisation registration wizard (blueprint 5.8 onboarding).
 * The sidebar tracks progress; each step is its own component, and the
 * organisation record itself is created after the profile + compliance
 * steps, so category agreements and team invites edit that record directly -
 * the same calls an admin would later make from the organisation-admin screen.
 */
@Component({
  selector: 'app-organisation-registration',
  standalone: true,
  imports: [
    AdminAccountStepComponent,
    OrgProfileStepComponent,
    ComplianceStepComponent,
    CategoryAgreementsStepComponent,
    InviteTeamStepComponent,
  ],
  templateUrl: './organisation-registration.component.html',
  styleUrl: './organisation-registration.component.css',
})
export class OrganisationRegistrationComponent {
  constructor(readonly state: OrganisationRegistrationStateService) {
    state.reset();
  }
}

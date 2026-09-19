import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../services/auth.service';
import { OrganisationRegistrationStateService } from '../organisation-registration-state.service';

/** Step 1: create the founding admin's account (organisation account type, no personal vault). */
@Component({
  selector: 'app-admin-account-step',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './admin-account-step.component.html',
  styleUrl: './admin-account-step.component.css',
})
export class AdminAccountStepComponent {
  private readonly auth = inject(AuthService);
  private readonly state = inject(OrganisationRegistrationStateService);

  adminName = '';
  email = '';
  password = '';

  readonly submitting = signal(false);
  readonly error = signal('');

  async submit(): Promise<void> {
    this.error.set('');
    this.submitting.set(true);
    try {
      const result = await this.auth.registerOrganisationAdmin(this.email.trim(), this.password, this.adminName.trim());
      if (!result.ok) {
        this.error.set(result.error ?? 'Could not create the admin account.');
        return;
      }

      this.state.adminUserId.set(this.auth.user()?.id ?? null);
      this.state.adminEmail.set(this.email.trim());
      this.state.next();
    } finally {
      this.submitting.set(false);
    }
  }
}

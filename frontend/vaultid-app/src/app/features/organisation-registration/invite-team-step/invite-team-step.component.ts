import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { VaultApiService } from '../../../services/vault-api.service';
import { OrganisationRegistrationStateService } from '../organisation-registration-state.service';

/**
 * Step 5 (final, optional): invite colleagues as additional org admins/operators.
 * Only captures emails on the org record - no invite email or accept flow yet.
 */
@Component({
  selector: 'app-invite-team-step',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './invite-team-step.component.html',
  styleUrl: './invite-team-step.component.css',
})
export class InviteTeamStepComponent {
  private readonly api = inject(VaultApiService);
  private readonly router = inject(Router);
  private readonly state = inject(OrganisationRegistrationStateService);

  emailsText = '';
  readonly submitting = signal(false);
  readonly error = signal('');

  private parsedEmails(): string[] {
    return this.emailsText
      .split(/[,\n]/)
      .map((e) => e.trim())
      .filter((e) => e.length > 0);
  }

  async finish(): Promise<void> {
    const organisationId = this.state.organisationId();
    const emails = this.parsedEmails();
    this.error.set('');

    if (organisationId && emails.length > 0) {
      this.submitting.set(true);
      try {
        await firstValueFrom(this.api.addPendingInvites(organisationId, emails));
      } catch {
        this.error.set('Could not save the invites, but your organisation is already registered - you can invite people later.');
        this.submitting.set(false);
        return;
      }
      this.submitting.set(false);
    }

    await this.router.navigate(['/organisation-admin']);
  }

  async skip(): Promise<void> {
    await this.router.navigate(['/organisation-admin']);
  }
}

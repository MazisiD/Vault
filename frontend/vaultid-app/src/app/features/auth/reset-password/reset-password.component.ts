import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../services/auth.service';

/**
 * Landing page for the link Supabase emails from resetPasswordForEmail().
 * Supabase's client picks up the recovery token from the URL automatically
 * and establishes a session before this component even loads - all this page
 * does is collect the new password and call updateUser({ password }).
 */
@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './reset-password.component.html',
  styleUrl: './reset-password.component.css',
})
export class ResetPasswordComponent {
  private readonly auth = inject(AuthService);

  password = '';
  readonly submitting = signal(false);
  readonly done = signal(false);
  readonly error = signal('');

  async submit(): Promise<void> {
    this.error.set('');
    this.submitting.set(true);
    try {
      const result = await this.auth.updatePassword(this.password);
      if (result.ok) {
        this.done.set(true);
      } else {
        this.error.set(result.error ?? 'Could not update your password. The link may have expired.');
      }
    } finally {
      this.submitting.set(false);
    }
  }
}

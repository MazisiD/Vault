import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../services/auth.service';

/**
 * Individual sign-up. Registering an organisation is a separate, multi-step
 * flow (see features/organisation-registration) since it needs an admin
 * account, an organisation profile, and compliance/agreement setup rather
 * than a single form.
 */
@Component({
  selector: 'app-register',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './register.component.html',
  styleUrl: './register.component.css',
})
export class RegisterComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  username = '';
  fullName = '';
  email = '';
  password = '';
  readonly submitting = signal(false);
  readonly error = signal('');
  readonly message = signal('');

  async submit(): Promise<void> {
    this.error.set('');
    this.message.set('');
    this.submitting.set(true);
    try {
      const result = await this.auth.register(
        this.username.trim(),
        this.email.trim(),
        this.password,
        'individual',
        this.fullName.trim(),
      );
      if (!result.ok) {
        this.error.set(result.error ?? 'Registration failed.');
        return;
      }

      if (this.auth.user()) {
        // Email confirmation is off for this project - the sign-up already
        // produced a live session, so go straight into the app.
        await this.router.navigate(['/vault']);
      } else {
        this.message.set('Check your email to confirm your account, then log in.');
      }
    } finally {
      this.submitting.set(false);
    }
  }
}

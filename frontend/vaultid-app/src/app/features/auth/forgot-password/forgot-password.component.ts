import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../services/auth.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.css',
})
export class ForgotPasswordComponent {
  private readonly auth = inject(AuthService);

  email = '';
  readonly submitting = signal(false);
  readonly sent = signal(false);
  readonly error = signal('');

  async submit(): Promise<void> {
    this.error.set('');
    this.submitting.set(true);
    try {
      const result = await this.auth.sendPasswordReset(this.email.trim());
      if (result.ok) {
        this.sent.set(true);
      } else {
        this.error.set(result.error ?? 'Something went wrong.');
      }
    } finally {
      this.submitting.set(false);
    }
  }
}

import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  identifier = '';
  password = '';
  readonly submitting = signal(false);
  readonly error = signal('');

  async submit(): Promise<void> {
    this.error.set('');
    this.submitting.set(true);
    try {
      const result = await this.auth.login(this.identifier.trim(), this.password);
      if (result.ok) {
        await this.router.navigate(['/vault']);
      } else {
        this.error.set(result.error ?? 'Login failed.');
      }
    } finally {
      this.submitting.set(false);
    }
  }
}

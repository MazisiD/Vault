import { Injectable, computed, inject } from '@angular/core';
import { AuthService } from './auth.service';

/**
 * Which vault is "active" - the authenticated user's own vault. This used to
 * be an arbitrary typed-in string (see git history); it's now the Supabase
 * auth user id, matching the identity the backend enforces from the JWT's
 * "sub" claim, so a signed-in user can only ever see their own vault.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly auth = inject(AuthService);

  readonly userId = computed(() => this.auth.user()?.id ?? '');
}

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/** Blocks vault/share/activity routes until a Supabase session exists. */
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.waitUntilReady();
  if (auth.user()) {
    return true;
  }
  return router.parseUrl('/login');
};

import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    loadComponent: () =>
      import('./features/auth/register/register.component').then((m) => m.RegisterComponent),
  },
  {
    path: 'register/organisation',
    loadComponent: () =>
      import('./features/organisation-registration/organisation-registration.component').then(
        (m) => m.OrganisationRegistrationComponent,
      ),
  },
  {
    path: 'organisation-admin',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/organisation-admin/organisation-admin.component').then((m) => m.OrganisationAdminComponent),
  },
  {
    path: 'forgot-password',
    loadComponent: () =>
      import('./features/auth/forgot-password/forgot-password.component').then((m) => m.ForgotPasswordComponent),
  },
  {
    path: 'forgot-username',
    loadComponent: () =>
      import('./features/auth/forgot-username/forgot-username.component').then((m) => m.ForgotUsernameComponent),
  },
  {
    path: 'reset-password',
    loadComponent: () =>
      import('./features/auth/reset-password/reset-password.component').then((m) => m.ResetPasswordComponent),
  },
  {
    path: 'vault',
    canActivate: [authGuard],
    loadComponent: () => import('./features/vault/vault.component').then((m) => m.VaultComponent),
  },
  {
    path: 'share',
    canActivate: [authGuard],
    loadComponent: () => import('./features/sharing/sharing.component').then((m) => m.SharingComponent),
  },
  {
    path: 'organisation',
    canActivate: [authGuard],
    loadComponent: () => import('./features/organisation/organisation.component').then((m) => m.OrganisationComponent),
  },
  {
    path: 'activity',
    canActivate: [authGuard],
    loadComponent: () => import('./features/activity/activity.component').then((m) => m.ActivityComponent),
  },
  { path: '**', redirectTo: 'login' },
];

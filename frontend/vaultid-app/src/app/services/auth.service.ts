import { Injectable, inject, signal } from '@angular/core';
import type { Session, User } from '@supabase/supabase-js';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { VaultApiService } from './vault-api.service';
import { SupabaseService } from './supabase.service';

export interface AuthResult {
  ok: boolean;
  error?: string;
}

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Registration, login, logout, and account recovery, all backed by Supabase
 * Auth directly (the Angular app talks to Supabase for these, not the
 * VaultID backend - see supabase/schema.sql for the username <-> email
 * lookup functions this relies on). "Forgot username" is the one exception:
 * Supabase has no concept of a username, so that goes through our own
 * backend endpoint instead.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly supabase = inject(SupabaseService).client;
  private readonly vaultApi = inject(VaultApiService);
  private readonly readyPromise: Promise<void>;

  readonly session = signal<Session | null>(null);
  readonly user = signal<User | null>(null);

  constructor() {
    this.readyPromise = this.supabase.auth.getSession().then(({ data }) => this.setSession(data.session));
    this.supabase.auth.onAuthStateChange((_event, session) => this.setSession(session));
  }

  /** Resolves once the initial session has been loaded from storage - await this before route guards decide anything. */
  waitUntilReady(): Promise<void> {
    return this.readyPromise;
  }

  get accessToken(): string | null {
    return this.session()?.access_token ?? null;
  }

  get username(): string {
    const meta = this.user()?.user_metadata as Record<string, unknown> | undefined;
    return (meta?.['username'] as string | undefined) ?? this.user()?.email ?? '';
  }

  get accountType(): 'individual' | 'organisation' {
    const meta = this.user()?.user_metadata as Record<string, unknown> | undefined;
    return (meta?.['account_type'] as string | undefined) === 'organisation' ? 'organisation' : 'individual';
  }

  get isOrganisation(): boolean {
    return this.accountType === 'organisation';
  }

  get organisationId(): string | null {
    const meta = this.user()?.user_metadata as Record<string, unknown> | undefined;
    return (meta?.['organisation_id'] as string | undefined) ?? null;
  }

  private setSession(session: Session | null): void {
    this.session.set(session);
    this.user.set(session?.user ?? null);
  }

  async register(
    username: string,
    email: string,
    password: string,
    accountType: 'individual' | 'organisation' = 'individual',
    displayName = '',
  ): Promise<AuthResult> {
    const metadata: Record<string, string> = {
      username,
      account_type: accountType,
    };

    if (accountType === 'organisation' && displayName) {
      metadata['organisation_name'] = displayName;
    }

    if (accountType === 'individual' && displayName) {
      metadata['full_name'] = displayName;
    }

    const { error } = await this.supabase.auth.signUp({
      email,
      password,
      options: { data: metadata },
    });

    if (error) {
      return { ok: false, error: error.message };
    }

    const userId = this.user()?.id;
    if (userId) {
      try {
        await firstValueFrom(this.vaultApi.createVault(userId, username));
      } catch (err) {
        const status = (err as { status?: number })?.status;
        if (status !== 409) {
          return {
            ok: false,
            error: 'Account created, but the vault could not be created. Please sign in and try again.',
          };
        }
      }
    }

    return { ok: true };
  }

  /**
   * Signs up the founding admin of a new organisation. Unlike an individual
   * sign-up this never creates a personal vault - the admin manages the
   * organisation record instead, linked afterwards via {@link linkOrganisation}
   * once it's been registered with the backend.
   */
  async registerOrganisationAdmin(email: string, password: string, adminName: string): Promise<AuthResult> {
    const { error } = await this.supabase.auth.signUp({
      email,
      password,
      options: { data: { account_type: 'organisation', role: 'admin', full_name: adminName } },
    });

    return error ? { ok: false, error: error.message } : { ok: true };
  }

  /** Links the signed-in admin's account to the organisation record created for them. */
  async linkOrganisation(organisationId: string): Promise<AuthResult> {
    const { error } = await this.supabase.auth.updateUser({ data: { organisation_id: organisationId } });
    return error ? { ok: false, error: error.message } : { ok: true };
  }

  async login(identifier: string, password: string): Promise<AuthResult> {
    let email = identifier;
    if (!EMAIL_PATTERN.test(identifier)) {
      const { data, error } = await this.supabase.rpc('get_email_for_identifier', { identifier });
      if (error || !data) {
        return { ok: false, error: 'No account found for that username.' };
      }
      email = data as string;
    }

    const { error } = await this.supabase.auth.signInWithPassword({ email, password });
    return error ? { ok: false, error: error.message } : { ok: true };
  }

  async logout(): Promise<void> {
    await this.supabase.auth.signOut();
  }

  /** Sends a Supabase password-reset email; the link lands on /reset-password. */
  async sendPasswordReset(email: string): Promise<AuthResult> {
    const { error } = await this.supabase.auth.resetPasswordForEmail(email, {
      redirectTo: `${window.location.origin}/reset-password`,
    });
    return error ? { ok: false, error: error.message } : { ok: true };
  }

  /** Called from the /reset-password page, once Supabase has established a recovery session from the emailed link. */
  async updatePassword(newPassword: string): Promise<AuthResult> {
    const { error } = await this.supabase.auth.updateUser({ password: newPassword });
    return error ? { ok: false, error: error.message } : { ok: true };
  }

  /** Backend-mediated: looks up the username for an email (via service role) and emails it, if an account matches. */
  async sendUsernameReminder(email: string): Promise<AuthResult> {
    try {
      const response = await fetch(`${environment.apiBaseUrl}/api/auth/forgot-username`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email }),
      });
      return response.ok ? { ok: true } : { ok: false, error: 'Something went wrong. Please try again.' };
    } catch {
      return { ok: false, error: 'Could not reach the server. Please try again.' };
    }
  }
}

# Supabase Auth setup

VaultID uses Supabase Auth for registration, login, and password reset. This
covers wiring an existing Supabase project into the app.

## 1. Import the schema

Open your Supabase project → **SQL Editor** → New query, paste in the
contents of [`supabase/schema.sql`](../supabase/schema.sql), and run it. This
creates:

- `public.profiles` - stores the username for each `auth.users` row (Supabase
  Auth itself only has an email).
- A trigger that creates a `profiles` row automatically on sign-up.
- `get_email_for_identifier(identifier)` - lets the frontend resolve a
  username *or* email typed at login into the email Supabase Auth needs.
- `get_username_for_email(email)` - used only by the backend (service role)
  for the "forgot username" flow.

## 2. Collect your project's keys

From your Supabase project → **Settings → API**:

| Value | Where it goes |
|---|---|
| Project URL | `frontend/vaultid-app/src/environments/environment.ts` → `supabaseUrl`, and `backend/VaultID.Api/appsettings.json` → `Supabase:Url` |
| anon / public key | `environment.ts` → `supabaseAnonKey` |
| service_role key (**secret** - never ship to the client) | `appsettings.json` → `Supabase:ServiceRoleKey`, or better, `dotnet user-secrets set Supabase:ServiceRoleKey "..."` |
| JWT Secret (Settings → API → JWT Settings) | `appsettings.json` → `Supabase:JwtSecret` (same caveat - prefer user-secrets) |

> If your project uses the newer asymmetric JWT signing keys instead of a
> shared secret, the `Supabase:JwtSecret` approach in `Program.cs` won't
> validate tokens - switch that JwtBearer configuration to JWKS/Authority-based
> validation against `{Supabase:Url}/auth/v1` instead.

## 3. (Optional) SMTP for the "forgot username" email

`Smtp:Host` in `appsettings.json` is empty by default, which makes the
backend log the "forgot username" email instead of sending it - handy for
local dev. Fill in `Smtp:Host`/`Port`/`User`/`Password`/`From` to actually
send it.

Password reset emails are sent by Supabase itself (its built-in email
templates under Authentication → Email Templates) - no SMTP config needed on
the backend for that one.

## How the pieces fit together

- **Register / Login / Logout / Password reset** - the Angular app talks
  directly to Supabase Auth via `@supabase/supabase-js`
  (`frontend/vaultid-app/src/app/services/auth.service.ts`). Login accepts a
  username or an email; a non-email-shaped identifier is first resolved to an
  email via `get_email_for_identifier`.
- **Forgot username** - the Angular app posts to the VaultID backend
  (`POST /api/auth/forgot-username`), whose `AuthController` is pure
  transport: it calls `AccountRecoveryService` (Application layer), which in
  turn calls `IAccountDirectoryStore` and `IEmailSender` - interfaces backed
  by real implementations in `services-layer/VaultID.Services/Remote`
  (`SupabaseAccountDirectoryStore` calls `get_username_for_email` with the
  service_role key; `SmtpEmailSender` sends the email). The Api layer never
  talks to Supabase or SMTP directly, matching every other data-access
  concern in this codebase - the service_role key and SMTP credentials only
  ever reach the services layer.
- **Authenticated API calls** - an HTTP interceptor
  (`interceptors/auth.interceptor.ts`) attaches the Supabase session's access
  token as `Authorization: Bearer <token>` on every call to the VaultID
  backend. The backend validates that JWT (`Program.cs`) and, for every
  vault-scoped endpoint, checks the route's `{userId}` against the token's
  `sub` claim (`Infrastructure/EnforceOwnVaultUserAttribute.cs`) so a signed-in
  user can never act on someone else's vault by editing the URL. (JWT
  validation itself stays in the Api layer since it's a transport/middleware
  concern, not a data-access call - it never contacts Supabase over the network.)

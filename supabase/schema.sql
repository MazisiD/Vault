-- VaultID — Supabase Auth schema
--
-- Import: paste this whole file into the Supabase SQL Editor (Project → SQL Editor → New query)
-- and run it once, or apply it with the Supabase CLI as a migration.
--
-- What this sets up:
--   - a `profiles` table holding the username for each Supabase Auth user
--     (auth.users only has an email, not a username)
--   - a trigger that creates a profile row automatically on signup
--   - get_email_for_identifier(identifier): lets the frontend resolve a
--     username OR email typed at login into the email Supabase Auth needs
--   - get_username_for_email(email): used by the backend (service role only)
--     to implement "forgot username" — never exposed to anon/authenticated
--     clients, to avoid username enumeration
--
-- Requires: Supabase Auth already enabled (default for every project).

create table if not exists public.profiles (
  id uuid primary key references auth.users (id) on delete cascade,
  username text not null,
  email text not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create unique index if not exists profiles_username_lower_idx
  on public.profiles (lower(username));

create unique index if not exists profiles_email_lower_idx
  on public.profiles (lower(email));

alter table public.profiles enable row level security;

drop policy if exists "Profiles are viewable by owner" on public.profiles;
create policy "Profiles are viewable by owner"
  on public.profiles for select
  using (auth.uid() = id);

drop policy if exists "Profiles are updatable by owner" on public.profiles;
create policy "Profiles are updatable by owner"
  on public.profiles for update
  using (auth.uid() = id);

-- Keep updated_at current on every profile edit.
create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists profiles_set_updated_at on public.profiles;
create trigger profiles_set_updated_at
  before update on public.profiles
  for each row execute procedure public.set_updated_at();

-- Auto-create a profile row whenever someone signs up via Supabase Auth.
-- Expects the client to pass a `username` in signUp's options.data, e.g.
--   supabase.auth.signUp({ email, password, options: { data: { username } } })
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer set search_path = public
as $$
begin
  insert into public.profiles (id, username, email)
  values (
    new.id,
    coalesce(new.raw_user_meta_data ->> 'username', split_part(new.email, '@', 1)),
    new.email
  );
  return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
  after insert on auth.users
  for each row execute procedure public.handle_new_user();

-- Keep profiles.email in sync if the user ever changes their auth email.
create or replace function public.handle_user_email_updated()
returns trigger
language plpgsql
security definer set search_path = public
as $$
begin
  if new.email is distinct from old.email then
    update public.profiles set email = new.email where id = new.id;
  end if;
  return new;
end;
$$;

drop trigger if exists on_auth_user_email_updated on auth.users;
create trigger on_auth_user_email_updated
  after update of email on auth.users
  for each row execute procedure public.handle_user_email_updated();

-- Login by username OR email: the frontend calls this first to resolve
-- whatever the user typed into the actual email, then signs in with that
-- email + the password via supabase.auth.signInWithPassword(...).
create or replace function public.get_email_for_identifier(identifier text)
returns text
language sql
stable
security definer set search_path = public
as $$
  select email from public.profiles
  where lower(username) = lower(identifier) or lower(email) = lower(identifier)
  limit 1;
$$;

grant execute on function public.get_email_for_identifier(text) to anon, authenticated;

-- Forgot username: given an email, return the associated username so the
-- backend can email it to them. Restricted to the service role (i.e. only
-- callable from the trusted backend with the service role key) so it can't
-- be used by anonymous clients to enumerate which emails have accounts.
create or replace function public.get_username_for_email(p_email text)
returns text
language sql
stable
security definer set search_path = public
as $$
  select username from public.profiles where lower(email) = lower(p_email) limit 1;
$$;

revoke all on function public.get_username_for_email(text) from public, anon, authenticated;
grant execute on function public.get_username_for_email(text) to service_role;

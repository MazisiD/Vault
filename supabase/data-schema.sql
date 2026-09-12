-- VaultID — application data schema
--
-- Import: paste into the Supabase SQL Editor and run once (after
-- supabase/schema.sql, which sets up auth/profiles). Covers every piece of
-- domain data the backend currently holds in memory.
--
-- IMPORTANT CONTEXT: VaultID is event-sourced. The `events` table below is
-- the single source of truth - it's what makes the "immutable activity feed"
-- in the product actually immutable. Categories, fields, field values, and
-- sharing grants are NOT independently authoritative; today they are
-- rebuilt from scratch by replaying a user's events every time they're read
-- (see backend/VaultID.Application/EventSourcing/VaultProjector.cs).
--
-- The five tables under "READ-MODEL / PROJECTION TABLES" mirror that
-- in-memory replay so you can query "Categories" and "Fields" as normal
-- tables (e.g. in the Supabase Table Editor, or for reporting) without
-- replaying events by hand. They are NOT wired up in the backend yet - the
-- backend still uses the in-memory stores for all of this. Turning these
-- into the backend's real persistence means implementing Postgres-backed
-- versions of IEventStore / IOrganisationStore / IWebhookSubscriptionStore
-- in services-layer/VaultID.Services (the only layer allowed to talk to the
-- database), which would append to `events` and upsert the projection tables
-- in the same transaction. Ask if you want that implemented next - this
-- file only prepares the schema for it.

-- ============================================================
-- EVENT STORE (source of truth)
-- ============================================================

create table if not exists public.events (
  id              uuid primary key default gen_random_uuid(),
  -- One stream per vault, keyed by the owning Supabase Auth user.
  stream_id       uuid not null references auth.users (id) on delete cascade,
  -- Event class name, e.g. "FieldUpdated", "CategoryCreated", "CategoryShared".
  type            text not null,
  -- The event's own properties, camelCase JSON (matches EventSerializer.cs).
  data            jsonb not null,
  metadata        jsonb,
  -- 1-based position within this stream; enforces optimistic concurrency.
  version         bigint not null,
  global_position bigserial,
  occurred_at     timestamptz not null default now(),
  unique (stream_id, version)
);

create index if not exists events_stream_version_idx on public.events (stream_id, version);
create index if not exists events_global_position_idx on public.events (global_position);
create index if not exists events_type_idx on public.events (type);

alter table public.events enable row level security;

drop policy if exists "Users can read their own events" on public.events;
create policy "Users can read their own events"
  on public.events for select
  using (auth.uid() = stream_id);

-- No insert/update/delete policy for anon/authenticated: every write must go
-- through the backend (service_role), since the unique(stream_id, version)
-- optimistic-concurrency check has to be enforced by the store implementation,
-- not by ad-hoc client writes racing each other.

-- ============================================================
-- ORGANISATION REGISTRY
-- ============================================================

create table if not exists public.organisations (
  id                             text primary key,
  name                           text not null,
  status                         text not null default 'pending', -- pending | approved | rejected
  agreement_id                   text,
  agreement_purpose              text,
  agreement_retention_days       int,
  agreement_legal_basis          text,
  agreement_third_party_sharing  text,
  agreement_deletion_commitment  text,
  registered_at                  timestamptz not null default now(),
  updated_at                     timestamptz not null default now()
);

create index if not exists organisations_name_idx on public.organisations (lower(name));

alter table public.organisations enable row level security;

-- The organisation directory is browsed by any signed-in user when choosing
-- who to share a category with (OrganisationsController has no [Authorize]
-- today), so it's readable by anyone; onboarding new organisations isn't a
-- user-facing action, so there's no insert/update policy here - that goes
-- through the backend's service_role client.
drop policy if exists "Organisations are publicly readable" on public.organisations;
create policy "Organisations are publicly readable"
  on public.organisations for select
  using (true);

-- ============================================================
-- WEBHOOK SUBSCRIPTIONS (organisation change-notification callbacks)
-- ============================================================

create table if not exists public.webhook_subscriptions (
  id              text primary key,
  organisation_id text not null references public.organisations (id) on delete cascade,
  -- The vault/user being watched.
  user_id         uuid not null references auth.users (id) on delete cascade,
  callback_url    text not null,
  created_at      timestamptz not null default now()
);

create index if not exists webhook_subscriptions_user_idx on public.webhook_subscriptions (user_id);
create index if not exists webhook_subscriptions_org_idx on public.webhook_subscriptions (organisation_id);

alter table public.webhook_subscriptions enable row level security;
-- Subscriptions are created by an organisation calling the org-facing API
-- (a separate, not-yet-built API-key auth model - see
-- OrganisationApiController), not by the vault owner's own session, so there
-- are no anon/authenticated policies here either; all access is via
-- service_role from the backend.

-- ============================================================
-- SHARE CODE INDEX (cross-vault lookup for code redemption)
-- ============================================================
-- Event streams are per-vault, so when an organisation presents a share code
-- we need a global index to resolve it to the issuing vault. Only the SHA-256
-- hash of the code is stored: the plaintext exists exactly once, in the
-- response shown to the user at generation time.

create table if not exists public.share_code_index (
  code_hash       text primary key,
  user_id         uuid not null references auth.users (id) on delete cascade,
  share_code_id   uuid not null,
  -- The only organisation permitted to redeem this code.
  organisation_id text not null references public.organisations (id) on delete cascade,
  expires_at      timestamptz not null,
  created_at      timestamptz not null default now()
);

create index if not exists share_code_index_expires_idx on public.share_code_index (expires_at);

alter table public.share_code_index enable row level security;
-- Redemption happens through the org-facing API using service_role; the vault
-- owner reads their codes from the projected vault state, never from this
-- index, so no anon/authenticated policies are granted here.

-- ============================================================
-- READ-MODEL / PROJECTION TABLES
-- (mirror VaultState - see the big comment at the top of this file)
-- ============================================================

create table if not exists public.categories (
  id         uuid primary key,
  user_id    uuid not null references auth.users (id) on delete cascade,
  name       text not null,
  is_system  boolean not null default false,
  created_at timestamptz not null default now(),
  unique (user_id, name)
);

create index if not exists categories_user_idx on public.categories (user_id);

create table if not exists public.field_definitions (
  id                          uuid primary key,
  user_id                     uuid not null references auth.users (id) on delete cascade,
  category_id                 uuid not null references public.categories (id) on delete cascade,
  -- Set only for a field nested one level inside a Group field; single-level nesting only.
  parent_field_definition_id  uuid references public.field_definitions (id) on delete cascade,
  name                        text not null,
  -- Text | LongText | Number | Date | Boolean | Choice | File | Group
  field_type                  text not null,
  -- One of the WHATWG autocomplete tokens (see Domain/Categories/AutocompleteTokens.cs), or null.
  autocomplete_token          text,
  -- Only meaningful when field_type = 'Choice'.
  choices                     text[],
  sort_order                  int not null default 0
);

create index if not exists field_definitions_category_idx on public.field_definitions (category_id);
create index if not exists field_definitions_parent_idx on public.field_definitions (parent_field_definition_id);

-- Field values are always stored as text; FieldValueValidator.cs validates
-- the string against the field's type at the application layer (e.g. must
-- parse as a number/date/bool, or be one of Choices) rather than the column
-- enforcing it.
create table if not exists public.field_values (
  user_id             uuid not null references auth.users (id) on delete cascade,
  field_definition_id uuid not null references public.field_definitions (id) on delete cascade,
  value               text,
  updated_at          timestamptz not null default now(),
  primary key (user_id, field_definition_id)
);

create table if not exists public.permission_grants (
  id                       uuid primary key,
  grantor_user_id          uuid not null references auth.users (id) on delete cascade,
  grantee_organisation_id  text not null references public.organisations (id) on delete cascade,
  category_id              uuid not null references public.categories (id) on delete cascade,
  scope                    text not null,   -- ReadOnly | ReadWithVerification
  duration                 text not null,   -- ThirtyDays | NinetyDays | OneYear | Indefinite | Custom
  agreement_id             text not null,
  expires_at               timestamptz,
  status                   text not null default 'Active', -- Active | Revoked | Expired | PendingRenewal
  -- Field-level restriction (share-code flow): the specific fields the user
  -- ticked. Null means the whole category is shared, which is how every grant
  -- created before the share-code flow behaves.
  field_definition_ids     uuid[],
  consented_at             timestamptz not null default now(),
  updated_at               timestamptz not null default now()
);

create index if not exists permission_grants_grantor_idx on public.permission_grants (grantor_user_id);
create index if not exists permission_grants_grantee_idx on public.permission_grants (grantee_organisation_id);
create index if not exists permission_grants_category_idx on public.permission_grants (category_id);

create table if not exists public.signed_agreements (
  user_id          uuid not null references auth.users (id) on delete cascade,
  organisation_id  text not null references public.organisations (id) on delete cascade,
  agreement_id     text not null,
  signed_at        timestamptz not null default now(),
  primary key (user_id, organisation_id)
);

-- Owner-only RLS on every projection table - same shape as `events` above.
alter table public.categories enable row level security;
alter table public.field_definitions enable row level security;
alter table public.field_values enable row level security;
alter table public.permission_grants enable row level security;
alter table public.signed_agreements enable row level security;

drop policy if exists "Users can read their own categories" on public.categories;
create policy "Users can read their own categories"
  on public.categories for select using (auth.uid() = user_id);

drop policy if exists "Users can read their own field definitions" on public.field_definitions;
create policy "Users can read their own field definitions"
  on public.field_definitions for select using (auth.uid() = user_id);

drop policy if exists "Users can read their own field values" on public.field_values;
create policy "Users can read their own field values"
  on public.field_values for select using (auth.uid() = user_id);

drop policy if exists "Users can read their own grants" on public.permission_grants;
create policy "Users can read their own grants"
  on public.permission_grants for select using (auth.uid() = grantor_user_id);

drop policy if exists "Users can read their own signed agreements" on public.signed_agreements;
create policy "Users can read their own signed agreements"
  on public.signed_agreements for select using (auth.uid() = user_id);

-- As with `events`, there are no anon/authenticated write policies: every
-- mutation is an event append handled by the backend (service_role), which
-- would keep these projection tables in sync in the same transaction.

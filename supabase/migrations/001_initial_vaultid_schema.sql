-- VaultID initial schema for the organisation share-code flow
-- Run this in Supabase SQL editor or apply as a migration in your deployment pipeline.
-- This file intentionally mirrors the app-facing tables used by the current service layer:
-- organisations, share_code_index, events, signed_agreements, permission_grants, and the
-- read-model tables used by the event projector.

create extension if not exists pgcrypto;

create table if not exists public.events (
  id              uuid primary key default gen_random_uuid(),
  stream_id       uuid not null references auth.users (id) on delete cascade,
  type            text not null,
  data            jsonb not null,
  metadata        jsonb,
  version         bigint not null,
  global_position bigserial,
  occurred_at     timestamptz not null default now(),
  unique (stream_id, version)
);

create index if not exists events_stream_version_idx on public.events (stream_id, version);
create index if not exists events_global_position_idx on public.events (global_position);
create index if not exists events_type_idx on public.events (type);

create table if not exists public.organisations (
  id                             text primary key,
  name                           text not null,
  status                         text not null default 'pending',
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

create table if not exists public.share_code_index (
  code_hash       text primary key,
  user_id         uuid not null references auth.users (id) on delete cascade,
  share_code_id   uuid not null,
  organisation_id text not null references public.organisations (id) on delete cascade,
  expires_at      timestamptz not null,
  created_at      timestamptz not null default now()
);

create index if not exists share_code_index_expires_idx on public.share_code_index (expires_at);

create table if not exists public.signed_agreements (
  user_id          uuid not null references auth.users (id) on delete cascade,
  organisation_id  text not null references public.organisations (id) on delete cascade,
  agreement_id     text not null,
  signed_at        timestamptz not null default now(),
  primary key (user_id, organisation_id)
);

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
  id                         uuid primary key,
  user_id                    uuid not null references auth.users (id) on delete cascade,
  category_id                uuid not null references public.categories (id) on delete cascade,
  parent_field_definition_id uuid references public.field_definitions (id) on delete cascade,
  name                       text not null,
  field_type                 text not null,
  autocomplete_token         text,
  choices                    text[],
  sort_order                 int not null default 0
);

create index if not exists field_definitions_category_idx on public.field_definitions (category_id);
create index if not exists field_definitions_parent_idx on public.field_definitions (parent_field_definition_id);

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
  scope                    text not null,
  duration                 text not null,
  agreement_id             text not null,
  expires_at               timestamptz,
  status                   text not null default 'Active',
  field_definition_ids     uuid[],
  consented_at             timestamptz not null default now(),
  updated_at               timestamptz not null default now()
);

create index if not exists permission_grants_grantor_idx on public.permission_grants (grantor_user_id);
create index if not exists permission_grants_grantee_idx on public.permission_grants (grantee_organisation_id);
create index if not exists permission_grants_category_idx on public.permission_grants (category_id);

create table if not exists public.webhook_subscriptions (
  id              text primary key,
  organisation_id text not null references public.organisations (id) on delete cascade,
  user_id         uuid not null references auth.users (id) on delete cascade,
  callback_url    text not null,
  created_at      timestamptz not null default now()
);

create index if not exists webhook_subscriptions_user_idx on public.webhook_subscriptions (user_id);
create index if not exists webhook_subscriptions_org_idx on public.webhook_subscriptions (organisation_id);

-- minimal RLS so the app can read public org directory and owner-only event streams.
alter table public.events enable row level security;
alter table public.organisations enable row level security;
alter table public.share_code_index enable row level security;
alter table public.webhook_subscriptions enable row level security;
alter table public.categories enable row level security;
alter table public.field_definitions enable row level security;
alter table public.field_values enable row level security;
alter table public.permission_grants enable row level security;
alter table public.signed_agreements enable row level security;

create policy if not exists "Users can read their own events"
  on public.events for select
  using (auth.uid() = stream_id);

create policy if not exists "Organisations are publicly readable"
  on public.organisations for select
  using (true);

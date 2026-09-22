-- Adds the organisation registration wizard's profile fields, per-category
-- agreement overrides, and pending team invites to the existing
-- `organisations` table (see 001_initial_vaultid_schema.sql).
-- Run this in the Supabase SQL editor or apply as a migration in your
-- deployment pipeline, against a database that already has 001 applied.

alter table public.organisations
  add column if not exists registration_number text,
  add column if not exists address             text,
  add column if not exists industry            text,
  add column if not exists contact_name        text,
  add column if not exists contact_phone       text,
  add column if not exists contact_email       text,
  add column if not exists category_agreements jsonb not null default '{}'::jsonb,
  add column if not exists pending_invites     jsonb not null default '[]'::jsonb;

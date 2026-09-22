# VaultID — Personal Data Vault Platform

A reference-not-copy personal data platform built on event sourcing and a category-based
permission engine. Individuals own a vault, share specific data categories with
organisations under explicit data-processing agreements, and see every access in a
transparent, immutable activity feed.

This document explains how to run **the whole system**. For details on an individual
layer, see the README in each folder.

## Problem Statement

People constantly hand over personal data — name, ID number, health records, education
history — to banks, universities, medical insurers, and other organisations, usually
through one-off forms. Once submitted, that data disappears into the organisation's own
systems: the individual has no way to see what was shared, with whom, for how long, or
to revoke access later. There's no single place where a person can manage their personal
information, control who can see specific categories of it, and audit every access —
while organisations still need a trustworthy, verifiable way to request and consume that
data (and prove they agreed to specific terms for holding it).

## Approach

VaultID flips the traditional model: instead of organisations holding and controlling
personal data, individuals own a single vault, and organisations get scoped, time-boxed,
revocable access to specific categories of it (Biographical, Health, Educational, etc.)
— never blanket access to everything. Every state change is modelled as an event rather
than an in-place update, so consent and access history are first-class, auditable facts
rather than an afterthought bolted onto a CRUD system.

## Solution

- **Event-sourced vault**: every change to a user's data and every grant/revoke/renew
  action is captured as an immutable event, projected into the current vault state, and
  surfaced as a transparent activity/audit feed the user can inspect at any time.
- **Category-based permission engine**: sharing happens at the category level under an
  explicit data-processing agreement — a user picks an organisation, reviews what they're
  agreeing to, and grants access for a defined duration; they can revoke or renew at any
  time, and the backend enforces every rule.
- **Two API surfaces**: a user-facing API (`/api/...`) for managing the vault, and an
  organisation-facing API (`/v1/...`) that lets an approved organisation read only what's
  been shared, verify a field without seeing its value, and subscribe to webhooks for
  change propagation — never more than it was granted.
- **Strict layered architecture**: a standalone data-access service layer with no
  backend dependencies, a Domain layer with events/enums/category catalog, an
  Application layer holding all business logic, and a thin API layer that's transport
  only. The Angular frontend only renders data and surfaces backend rejections.
- **Real auth**: Supabase Auth backs registration/login, JWTs are validated server-side,
  and a custom guard ensures a signed-in user can never access another user's vault by
  tampering with the URL.

## Tech Stack

**Backend**
- C# / .NET 10, ASP.NET Core Web API (thin controllers, no business logic)
- Custom event-sourcing (event store, projections) — no external ES framework
- JWT bearer authentication (`Microsoft.AspNetCore.Authentication.JwtBearer`) validating Supabase-issued tokens
- OpenAPI / Swagger UI (`Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore.SwaggerUI`)
- In-memory persistence today, with an interface-based data-access layer (`VaultID.Services`) designed for a drop-in Supabase Postgres implementation
- xUnit-style test project (`VaultID.Tests`) covering Domain/Application/Api

**Frontend**
- Angular 19 (standalone components, one component = folder with `.ts`/`.html`/`.css`/`.spec.ts`)
- TypeScript, RxJS
- `@supabase/supabase-js` for direct Auth calls (login/register/password reset)
- Karma + Jasmine for unit tests

**Auth & Data**
- Supabase Auth (JWT issuance, email/password, password reset)
- Supabase Postgres (schema in `supabase/`, migrations tracked in `supabase/migrations/`)

**Tooling**
- `run_vaultid.py` — one-shot Python script to install, build, and run backend + frontend together
- Solution/workspace split via `.slnx` files (`VaultID.slnx`, `VaultID.Services.slnx`)

## Architecture (4 strictly separated layers)

```
/VaultID
  /services-layer
    /VaultID.Services    ← Data access ONLY. Standalone, NuGet-ready, no backend refs.
  /backend
    /VaultID.Domain      ← Event & model definitions, enums, category catalog
    /VaultID.Application ← ALL business logic (event sourcing, permissions, sharing)
    /VaultID.Api         ← Thin ASP.NET Core Web API (transport only)
  /frontend
    /vaultid-app         ← Angular presentation (render + HTTP only, no logic)
```

Dependency direction: `Api → Application → Domain` and `Application → service-layer
interfaces`. The service layer depends on **nothing** in the backend. Business logic lives
**only** in `VaultID.Application`.

Persistence is currently **in-memory** with `// TODO: Replace with Supabase Postgres`
markers; swapping to a real database is a single new DI registration.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (LTS) + npm
- [Python 3.9+](https://www.python.org/downloads/) (only for the one-shot setup script)

## Quick start — one script does everything

From the repository root:

```bash
python run_vaultid.py
```

The script runs every step in order: checks prerequisites, restores and builds the
service layer, restores and builds the backend, installs the frontend npm packages, then
starts the **API on http://localhost:5080** and the **Angular dev server on
http://localhost:4200**, streaming both logs (`[api]` / `[web]`) in one terminal. Press
**Ctrl+C** to stop both.

Options:

| Command | What it does |
|---|---|
| `python run_vaultid.py` | Full install + build, then run both servers |
| `python run_vaultid.py --setup-only` | Install and build only; start nothing |
| `python run_vaultid.py --run-only` | Skip install/build; just start both servers |
| `python run_vaultid.py --with-tests` | Also run backend and frontend unit tests |
| `python run_vaultid.py --build-frontend` | Also produce a production frontend build |

If `npm install` fails against a corporate registry, the script automatically retries
against the public npm registry.

Once it is up, open **http://localhost:4200** and jump to [Use it](#use-it).

## Run everything manually

Prefer to drive it yourself? Run the backend and frontend in **two separate terminals**.
(The service layer is a library compiled into the backend — it is not started on its own.)

### Terminal 1 — Backend API

```bash
cd backend/VaultID.Api
dotnet run
```

Serves on **http://localhost:5080**, seeds demo organisations, and allows CORS from the
Angular dev server.

### Terminal 2 — Frontend

```bash
cd frontend/vaultid-app
npm install        # first time only (see frontend/README.md if a registry error occurs)
npm start
```

Serves on **http://localhost:4200**.

### Use it

1. Open **http://localhost:4200**.
2. Set an active user in the top bar (the vault is created automatically on first load).
3. **My Vault** — view/edit fields per category (Biographical, Health, Educational).
4. **Share** — search organisations, review their data-processing agreement, then share a
   category for a chosen duration; revoke or renew existing grants.
5. **Activity** — watch the immutable, event-sourced audit feed update as you act.

## Build/verify each part

```bash
# Service layer
cd services-layer && dotnet build VaultID.Services.slnx -c Release

# Backend (Domain + Application + Api)
cd backend && dotnet build VaultID.slnx -c Release

# Frontend
cd frontend/vaultid-app && npx ng build
```

## Layer-specific docs

- [`services-layer/README.md`](services-layer/README.md) — data access layer / NuGet packaging
- [`backend/README.md`](backend/README.md) — domain, application, API, endpoint reference
- [`frontend/vaultid-app/README.md`](frontend/vaultid-app/README.md) — Angular app

# VaultID — Personal Data Vault Platform

A reference-not-copy personal data platform built on event sourcing and a category-based
permission engine. Individuals own a vault, share specific data categories with
organisations under explicit data-processing agreements, and see every access in a
transparent, immutable activity feed.

This document explains how to run **the whole system**. For details on an individual
layer, see the README in each folder.

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

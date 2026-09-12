# VaultID Frontend — vaultid-app

The **presentation layer**: an Angular application that renders data and forwards user
actions to the backend over HTTP. It contains **no business logic** — all validation,
permission decisions, and rules live in the backend. Components only display whatever the
API returns and call API methods.

## Prerequisites

- [Node.js](https://nodejs.org/) (LTS) and npm
- The backend API running on **http://localhost:5080** (see `backend/README.md`)

## Install dependencies

If installing from a corporate npm registry causes JSON/truncation errors, use the public
registry:

```bash
cd frontend/vaultid-app
npm install --no-audit --no-fund --legacy-peer-deps --registry=https://registry.npmjs.org
```

Otherwise:

```bash
cd frontend/vaultid-app
npm install
```

## Run (dev server)

```bash
npm start
```

This serves the app on **http://localhost:4200** with live reload. The API base URL is
configured in `src/environments/environment.ts` (`apiBaseUrl: 'http://localhost:5080'`).

## Build (production)

```bash
npm run build
# or: npx ng build
```

Output is written to `dist/vaultid-app/`.

## Structure

```
src/app/
  services/
    vault-api.service.ts   ← the ONLY place that talks to the backend (HTTP only)
    session.service.ts     ← UI-only state (active userId in localStorage)
  features/
    vault/                 ← view/edit vault fields by category
    sharing/               ← search orgs, review agreements, share/revoke/renew grants
    activity/              ← event-sourced activity timeline
  app.routes.ts            ← lazy-loaded routes: /vault, /share, /activity
```

## Note

The app needs the backend running to load data. With the API up on port 5080, open
http://localhost:4200, set an active user in the top bar, and the vault is created on
first load.

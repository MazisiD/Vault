# VaultID Backend — Domain, Application, API

The backend holds **all business logic** across three projects, plus the HTTP entry point.

| Project | Responsibility |
|---|---|
| `VaultID.Domain` | Event/model definitions, enums, category catalog. No dependencies. |
| `VaultID.Application` | All business logic: event sourcing, permission engine, sharing rules, projections. Depends on Domain + the service-layer **interfaces** only. |
| `VaultID.Api` | Thin ASP.NET Core Web API. Transport only — no business logic. |

The Application layer talks to the data-access layer purely through interfaces
(`IEventStore`, `IOrganisationStore`, `IWebhookSubscriptionStore`). The `EventSerializer`
bridges domain events to the opaque `StoredEvent` records the service layer stores.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- The `services-layer` project must be present (the API references it via the solution).

## Build

```bash
cd backend
dotnet build VaultID.slnx -c Release
```

## Run the API

```bash
cd backend/VaultID.Api
dotnet run
```

The API starts on **http://localhost:5080** (see `Properties/launchSettings.json`).
CORS is pre-configured to allow the Angular dev server at `http://localhost:4200`.
Demo organisations (FNB Bank, University of Cape Town, Discovery Health) are seeded on
startup.

OpenAPI document: **http://localhost:5080/openapi/v1.json**

## Key endpoints

User-facing (`/api/...`):

- `POST /api/vaults` — create a vault
- `GET  /api/vaults/{userId}` — vault summary
- `GET  /api/vaults/{userId}/categories/{category}` — read a category
- `PUT  /api/vaults/{userId}/fields` — update a field
- `GET  /api/vaults/{userId}/activity` — event-sourced audit feed
- `GET  /api/vaults/{userId}/grants` — list grants
- `POST /api/vaults/{userId}/shares` — share a category
- `POST /api/vaults/{userId}/shares/{grantId}/revoke|renew` — manage a grant
- `GET  /api/organisations` — search organisations
- `GET  /api/organisations/{id}/agreement` — data processing agreement
- `GET  /api/metadata/categories` — category/field catalog

Organisation-facing (`/v1/...`, requires `X-Org-Id` header):

- `GET  /v1/vault/{userId}/categories/{category}` — read shared data (403 if not granted)
- `GET  /v1/vault/{userId}/fields/{field}` — read a single field
- `POST /v1/vault/{userId}/verify/{field}` — verify without disclosing
- `POST /v1/webhooks` / `DELETE /v1/webhooks/{id}` — change-propagation subscriptions
- `GET  /v1/grants` — grants held by the calling organisation

## Quick smoke test

```bash
# create a vault
curl -X POST http://localhost:5080/api/vaults \
  -H "Content-Type: application/json" \
  -d '{"userId":"alice","displayName":"Alice"}'

# update a field
curl -X PUT http://localhost:5080/api/vaults/alice/fields \
  -H "Content-Type: application/json" \
  -d '{"category":"Biographical","field":"FullName","value":"Alice Smith"}'

# read it back
curl http://localhost:5080/api/vaults/alice/categories/Biographical
```

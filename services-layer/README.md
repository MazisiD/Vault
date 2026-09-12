# VaultID.Services — Service (Data Access) Layer

This is the **data access layer**. It is a standalone .NET class library, completely
independent of the backend, and is designed to later be published as a NuGet package.

It contains **only persistence concerns**: event storage, organisation records, and
webhook subscriptions. There is **no business logic, no domain types, and no references
to the backend**. Events are stored as opaque records (`StoredEvent`: a string type
discriminator + a JSON payload), so this layer never needs to know what an event means.

Currently it uses thread-safe **in-memory** stores. Every store carries a
`// TODO: Replace with Supabase Postgres` marker plus the target table schema, so a real
database can be swapped in via a new DI registration without touching any other layer.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Project layout

```
services-layer/
  VaultID.Services.slnx        ← solution
  VaultID.Services/
    VaultID.Services.csproj    ← class library (IsPackable=true, PackageId=VaultID.Services)
    Abstractions/              ← IEventStore, IOrganisationStore, IWebhookSubscriptionStore
    InMemory/                  ← in-memory implementations (Supabase TODO markers)
    Persistence/               ← StoredEvent, OrganisationRecord, WebhookSubscriptionRecord
    DependencyInjection/       ← AddVaultIdServices()
```

## Build

```bash
cd services-layer
dotnet build VaultID.Services.slnx -c Release
```

## Pack as a NuGet package (optional)

```bash
cd services-layer/VaultID.Services
dotnet pack -c Release -o ./nupkg
```

The `.nupkg` will be written to `services-layer/VaultID.Services/nupkg/`.

## How it is consumed

This layer is **not run on its own** — it has no entry point. The backend's
Application layer references it and registers its stores during startup:

```csharp
services.AddVaultIdServices();   // registers the in-memory stores as singletons
```

When moving to Supabase, add a parallel extension (e.g. `AddVaultIdSupabaseServices()`)
that registers Postgres-backed implementations of the same interfaces. No other layer
changes.

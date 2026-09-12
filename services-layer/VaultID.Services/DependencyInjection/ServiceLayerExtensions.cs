using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VaultID.Services.Abstractions;
using VaultID.Services.Configuration;
using VaultID.Services.InMemory;
using VaultID.Services.Postgres;
using VaultID.Services.Remote;

namespace VaultID.Services.DependencyInjection;

/// <summary>
/// Composition root for the data-access layer. A consuming backend calls
/// either <c>services.AddVaultIdServices()</c> (in-memory, e.g. for tests) or
/// <c>services.AddVaultIdPostgresServices(connectionString)</c> (real Supabase
/// Postgres persistence - see supabase/data-schema.sql) and receives the same
/// persistence abstractions wired to whichever backing implementation it chose.
/// No backend code needs to change either way, since it depends only on the
/// interfaces.
/// </summary>
public static class ServiceLayerExtensions
{
    /// <summary>
    /// Registers the VaultID data-access layer using the in-memory simulated
    /// persistence. Stores are singletons so data survives for the process.
    /// </summary>
    public static IServiceCollection AddVaultIdServices(this IServiceCollection services)
    {
        // TODO: Swap these singletons for Supabase Postgres-backed, scoped
        // implementations once the database layer is integrated.
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        services.AddSingleton<IOrganisationStore, InMemoryOrganisationStore>();
        services.AddSingleton<IWebhookSubscriptionStore, InMemoryWebhookSubscriptionStore>();
        services.AddSingleton<IShareCodeIndexStore, InMemoryShareCodeIndexStore>();

        return services;
    }

    /// <summary>
    /// Registers the VaultID data-access layer against a real Postgres
    /// database (a Supabase project's "Direct connection string", found under
    /// Settings → Database) using the tables in supabase/data-schema.sql.
    /// Use this instead of <see cref="AddVaultIdServices"/> for real persistence.
    /// </summary>
    public static IServiceCollection AddVaultIdPostgresServices(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<IEventStore, PostgresEventStore>();
        services.AddSingleton<IOrganisationStore, PostgresOrganisationStore>();
        services.AddSingleton<IWebhookSubscriptionStore, PostgresWebhookSubscriptionStore>();
        services.AddSingleton<IShareCodeIndexStore, PostgresShareCodeIndexStore>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IAccountDirectoryStore"/> backed by a real call to
    /// Supabase's PostgREST endpoint (service_role key). Used for the "forgot
    /// username" flow - see supabase/schema.sql for the function it calls.
    /// </summary>
    public static IServiceCollection AddVaultIdSupabaseAccountDirectory(
        this IServiceCollection services, Action<SupabaseServiceOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddHttpClient<IAccountDirectoryStore, SupabaseAccountDirectoryStore>();
        return services;
    }

    /// <summary>Registers <see cref="IEmailSender"/> backed by real SMTP delivery.</summary>
    public static IServiceCollection AddVaultIdSmtpEmailSender(
        this IServiceCollection services, Action<SmtpOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using VaultID.Application.EventSourcing;
using VaultID.Application.Permissions;
using VaultID.Application.Services;
using VaultID.Application.Sharing;
using VaultID.Services.Configuration;
using VaultID.Services.DependencyInjection;

namespace VaultID.Application.DependencyInjection;

/// <summary>
/// Composition root for the business-logic layer. Registers the use-case
/// services and the event-sourcing plumbing, and pulls in the data-access
/// layer. The Api layer only needs to call <c>AddVaultIdApplication()</c> (or
/// its connection-string overload for real Postgres persistence).
/// </summary>
public static class ApplicationExtensions
{
    /// <summary>
    /// Wires the backend to the in-memory data-access layer. Used for local
    /// development without a database configured, and by the integration
    /// tests, so they don't need a real Postgres instance.
    /// </summary>
    public static IServiceCollection AddVaultIdApplication(this IServiceCollection services)
    {
        services.AddVaultIdServices();
        return AddApplicationCore(services);
    }

    /// <summary>
    /// Wires the backend to real Supabase Postgres persistence (see
    /// supabase/data-schema.sql) instead of the in-memory stores.
    /// </summary>
    /// <param name="postgresConnectionString">
    /// The Supabase project's "Direct connection string" (Settings → Database).
    /// </param>
    public static IServiceCollection AddVaultIdApplication(this IServiceCollection services, string postgresConnectionString)
    {
        services.AddVaultIdPostgresServices(postgresConnectionString);
        return AddApplicationCore(services);
    }

    private static IServiceCollection AddApplicationCore(IServiceCollection services)
    {
        // Event-sourcing plumbing.
        services.AddSingleton<EventSerializer>();
        services.AddScoped<VaultStreamRepository>();
        services.AddSingleton<PermissionEngine>();

        // Redemption throttling is process-wide state, so it must be a singleton
        // for the counters to mean anything across requests.
        services.AddSingleton<ShareCodeRateLimiter>();

        // Use-case services (business logic).
        services.AddScoped<VaultService>();
        services.AddScoped<SharingService>();
        services.AddScoped<ShareCodeService>();
        services.AddScoped<OrganisationRegistryService>();
        services.AddScoped<OrganisationDataAccessService>();
        services.AddScoped<ActivityService>();
        services.AddScoped<WebhookNotificationService>();
        services.AddScoped<GrantQueryService>();
        services.AddScoped<CategoryMetadataService>();
        services.AddScoped<CategorySchemaService>();

        return services;
    }

    /// <summary>
    /// Registers the "forgot username" account-recovery use case, backed by a
    /// real call to Supabase (service_role) and real SMTP delivery. Kept
    /// separate from <see cref="AddVaultIdApplication(IServiceCollection)"/>
    /// since it needs secrets the Api layer's configuration supplies, rather
    /// than being wired up unconditionally like the in-memory stores.
    /// </summary>
    public static IServiceCollection AddVaultIdAccountRecovery(
        this IServiceCollection services,
        Action<SupabaseServiceOptions> configureSupabase,
        Action<SmtpOptions> configureSmtp)
    {
        services.AddVaultIdSupabaseAccountDirectory(configureSupabase);
        services.AddVaultIdSmtpEmailSender(configureSmtp);
        services.AddScoped<AccountRecoveryService>();

        return services;
    }
}

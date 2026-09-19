using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VaultID.Services.Abstractions;
using VaultID.Services.InMemory;

namespace VaultID.Tests.Api;

/// <summary>
/// Swaps the app's real Supabase JWT authentication for <see cref="TestAuthHandler"/>,
/// so integration tests can authenticate as an arbitrary user id via a header
/// instead of minting real signed JWTs.
///
/// It also swaps the app's Postgres persistence for the in-memory stores. The
/// real <c>events.stream_id</c> column is a foreign key onto <c>auth.users</c>,
/// so the invented user ids these tests use could never be written to the live
/// database - and tests should exercise the code rather than the network.
/// </summary>
public sealed class VaultIdWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.RemoveAll<IEventStore>();
            services.RemoveAll<IOrganisationStore>();
            services.RemoveAll<IWebhookSubscriptionStore>();
            services.RemoveAll<IShareCodeIndexStore>();

            services.AddSingleton<IEventStore, InMemoryEventStore>();
            services.AddSingleton<IOrganisationStore, InMemoryOrganisationStore>();
            services.AddSingleton<IWebhookSubscriptionStore, InMemoryWebhookSubscriptionStore>();
            services.AddSingleton<IShareCodeIndexStore, InMemoryShareCodeIndexStore>();
        });
    }
}

using Constants;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GeoClubBot.Tests.Integration.E2E;

/// <summary>
/// Boots the real API in-process (Program.cs → controllers, middleware, MediatR, EF) against
/// the shared Postgres Testcontainer. Background adapters that would reach outside the process
/// (the Discord gateway connection and Quartz cron jobs) are stripped so the host starts cleanly
/// and only the HTTP surface is exercised.
/// </summary>
public sealed class GeoClubBotApiFactory(string connectionString, Guid mainClubId)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Added last, so these win over appsettings.json (loaded from the API content root).
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ConfigKeys.PostgresConnectionString}"] = connectionString,
                // These tests don't call GeoGuessr; keep the real client (no outbound HTTP at
                // startup) rather than the mock, which would also try to launch a browser.
                ["GeoGuessr:UseMock"] = "false",
                // The shared fixture already applied migrations.
                ["SQL:Migrate"] = "false",
                // appsettings.json ships a placeholder ClubId ("your-club-id") that isn't a valid
                // Guid; supply a real, test-scoped one so options binding + the controller work.
                ["GeoGuessr:Clubs:0:ClubId"] = mainClubId.ToString(),
                ["GeoGuessr:Clubs:0:IsMain"] = "true",
                ["GeoGuessr:Clubs:0:NcfaToken"] = "test-ncfa-token",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Strip every background worker the app would normally start: the Discord gateway
            // login, the self-roles updater, the Discord member-join/leave handlers, and the
            // initial GeoGuessr sync (all in our GeoClubBot.* assemblies), plus the Quartz cron
            // scheduler. Left running, InitialSyncService in particular blocks host startup while
            // it calls GeoGuessr. Framework hosted services (Kestrel/TestServer, health checks,
            // OpenTelemetry, auto-activation) live in other assemblies and are left intact.
            var backgroundServices = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && d.ImplementationType is { } impl
                            && (impl.Assembly.GetName().Name?.StartsWith("GeoClubBot", StringComparison.Ordinal) == true
                                || impl.Namespace?.StartsWith("Quartz", StringComparison.Ordinal) == true))
                .ToList();

            foreach (var descriptor in backgroundServices)
            {
                services.Remove(descriptor);
            }

            // Repoint EF at the Testcontainer. The DbContext is registered during Program startup
            // from the connection string read off builder.Configuration, before the factory's
            // config overrides merge — so re-register it here to be certain it hits the test DB
            // (already migrated by PostgresFixture) rather than the appsettings placeholder.
            services.RemoveAll<DbContextOptions<GeoClubBotDbContext>>();
            services.RemoveAll<GeoClubBotDbContext>();
            services.AddDbContext<GeoClubBotDbContext>(options => options.UseNpgsql(connectionString));
        });
    }
}

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
/// <param name="runScheduler">
/// Keeps the Quartz hosted service alive instead of stripping it, for tests that exercise the
/// scheduler itself. Every cron schedule is then rewritten to a date that never arrives, so jobs
/// only run when a test triggers them by hand — a live schedule would otherwise fire real jobs
/// against GeoGuessr mid-test, on whatever cadence appsettings.json happens to carry.
/// </param>
public sealed class GeoClubBotApiFactory(string connectionString, Guid mainClubId, bool runScheduler = false)
    : WebApplicationFactory<Program>
{
    /// <summary>1 January 2100 — a valid cron expression whose next fire time is beyond any test run.</summary>
    private const string NeverFires = "0 0 0 1 1 ? 2100";

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

            if (runScheduler)
            {
                // The schedules live under a dozen unrelated config sections, so they are found the
                // way the job scanner finds them — by key — rather than listed here, where a newly
                // added job would be missed and would start firing for real inside the test host.
                var schedules = config.Build().AsEnumerable()
                    .Where(entry => entry.Key.Contains("Schedule", StringComparison.Ordinal)
                                    && !string.IsNullOrEmpty(entry.Value))
                    .ToDictionary(entry => entry.Key, _ => (string?)NeverFires);

                config.AddInMemoryCollection(schedules);
            }
        });

        builder.ConfigureTestServices(services =>
        {
            // Strip every background worker the app would normally start: the Discord gateway
            // login, the self-roles updater, the entry-point ("Launch") command config + gateway
            // listener, the Discord channel log sink, and the initial GeoGuessr sync (all in our
            // GeoClubBot.* assemblies), plus the Quartz cron scheduler. Left running, InitialSyncService
            // blocks host startup while it calls GeoGuessr, and EntryPointCommandConfigurationService
            // deadlocks it outright: its StartAsync awaits the Discord "ready" signal that only the
            // (stripped) gateway login can ever fire. Framework hosted services (Kestrel/TestServer,
            // health checks, OpenTelemetry, auto-activation) live in other assemblies and stay intact.
            //
            // Some of these are registered with a factory delegate (AddHostedService(p => ...)) rather
            // than AddHostedService<T>(), so their ServiceDescriptor.ImplementationType is null. Fall
            // back to the factory delegate's declaring type — the compiler-generated closure lives in
            // the assembly that registered it — so those are matched too.
            var backgroundServices = services
                .Where(d => d.ServiceType == typeof(IHostedService) && IsOwnBackgroundService(d))
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

    /// <summary>
    /// True when the hosted-service descriptor originates from one of our GeoClubBot.* assemblies, or
    /// from the Quartz scheduler unless <c>runScheduler</c> asked to keep it. Handles both
    /// <c>AddHostedService&lt;T&gt;()</c> (ImplementationType set)
    /// and the factory form <c>AddHostedService(p =&gt; ...)</c> (ImplementationType null — the type is
    /// recovered from the delegate's declaring type, i.e. the compiler-generated closure).
    /// </summary>
    private bool IsOwnBackgroundService(ServiceDescriptor descriptor)
    {
        var implementationType = descriptor.ImplementationType
                                 ?? descriptor.ImplementationInstance?.GetType()
                                 ?? descriptor.ImplementationFactory?.Method.DeclaringType;

        if (implementationType is null)
        {
            return false;
        }

        if (implementationType.Namespace?.StartsWith("Quartz", StringComparison.Ordinal) == true)
        {
            return !runScheduler;
        }

        return implementationType.Assembly.GetName().Name?.StartsWith("GeoClubBot", StringComparison.Ordinal) == true;
    }
}

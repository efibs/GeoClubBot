using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GeoClubBot.Services;
using Infrastructure;
using Infrastructure.InputAdapters;
using Infrastructure.InputAdapters.Jobs;
using Infrastructure.OutputAdapters;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl.AdoJobStore;
using QuartzExtensions;
using UseCases;
using UseCases.InputPorts;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using RunMode = Discord.Interactions.RunMode;

namespace GeoClubBot;

/// <summary>
/// Helper class to register all required services in the dependency injection
/// </summary>
public static class DependencyInjection
{
    public static void AddClubBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Add the discord socket client
        services.AddSingleton<DiscordSocketClient>(_ => new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged
        }));

        // Add the interaction service
        services.AddSingleton(p => new InteractionService(p.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig
            {
                DefaultRunMode = RunMode.Async
            }));

        // Add the logging service and instantiate it immediately and 
        // therefore registering the logging callbacks.
        services.AddActivatedSingleton<DiscordLoggingService>();

        // Add the discord bot service
        services.AddSingleton<DiscordBotService>();
        services.AddHostedService(p => p.GetRequiredService<DiscordBotService>());

        // Add the command handler
        services.AddActivatedSingleton<InteractionHandler>();

        // Get the geoguessr token
        var geoGuessrToken = configuration.GetValue<string>(ConfigKeys.GeoGuessrTokenConfigurationKey);

        // Sanity check
        if (string.IsNullOrWhiteSpace(geoGuessrToken))
        {
            throw new InvalidOperationException("GeoGuessrToken is not set");
        }

        // Add the http client
        services.AddHttpClient(HttpClientConstants.GeoGuessrHttpClientName, client =>
        {
            // Set the base address
            client.BaseAddress = new Uri(HttpClientConstants.GeoGuessrBaseUrl);

            // Set the token in the cookies
            client.DefaultRequestHeaders.Add("Cookie", $"_ncfa={geoGuessrToken}");
        });

        // Add auxiliary services
        services.AddSingleton<DiscordBotReadyService>();
        
        // Add the input adapters
        services.AddHostedService<ActivityCheckService>();
        services.AddHostedService<CheckClubLevelService>();
        services.AddHostedService<InitialSyncService>();

        // Add the output adapters 
        services.AddTransient<IGeoGuessrAccess, HttpGeoGuessrAccess>();
        services.AddTransient<IClubRepository, EfClubRepository>();
        services.AddTransient<IClubMemberRepository, EfClubMemberRepository>();
        services.AddTransient<IHistoryRepository, EfHistoryRepository>();
        services.AddTransient<IExcusesRepository, EfExcusesRepository>();
        services.AddTransient<IStrikesRepository, EfStrikesRepository>();
        services.AddTransient<IClubChallengeRepository, EfClubChallengeRepository>();
        services.AddTransient<IActivityStatusMessageSender, DiscordActivityStatusMessageSender>();
        services.AddTransient<IStatusUpdater, DiscordStatusUpdater>();
        services.AddTransient<IMessageSender, DiscordMessageSender>();

        // Add the use cases
        services.AddTransient<ICheckGeoGuessrPlayerActivityUseCase, CheckGeoGuessrPlayerActivityUseCase>();
        services.AddTransient<IReadMemberStrikesUseCase, ReadMemberStrikesUseCase>();
        services.AddTransient<IAddExcuseUseCase, AddExcuseUseCase>();
        services.AddTransient<IRemoveExcuseUseCase, RemoveExcuseUseCase>();
        services.AddTransient<IReadExcusesUseCase, ReadExcusesUseCase>();
        services.AddTransient<ICleanupUseCase, CleanupUseCase>();
        services.AddTransient<IGetLastCheckTimeUseCase, GetLastCheckTimeUseCase>();
        services.AddSingleton<ICheckClubLevelUseCase, CheckClubLevelUseCase>();
        services.AddTransient<IReadOrSyncClubMemberUseCase, ReadOrSyncClubMemberUseCase>();
        services.AddTransient<ISyncClubUseCase, SyncClubUseCase>();
        services.AddTransient<ICheckStrikeDecayUseCase, CheckStrikeDecayUseCase>();
        services.AddTransient<IRevokeStrikeUseCase, RevokeStrikeUseCase>();
        services.AddTransient<IUnrevokeStrikeUseCase, UnrevokeStrikeUseCase>();
        services.AddTransient<IAddStrikeUseCase, AddStrikeUseCase>();
        services.AddTransient<IDailyChallengeUseCase, DailyChallengeUseCase>();
        
        // Get the connection string
        var connectionString = configuration.GetConnectionString(ConfigKeys.PostgresConnectionString)!;
        
        // Add the db context
        services.AddDbContext<GeoClubBotDbContext>(options => 
            options.UseNpgsql(connectionString));
        
        // Add the quartz scheduler
        services.AddQuartz(q =>
        {
            // Set the scheduler name
            q.SchedulerId = StringConstants.QuartzSchedulerName;

            // Get the assembly containing the jobs
            var commandsAssembly = typeof(JobAssemblyMarker).Assembly;
            
            // Detect and add the jobs automatically
            q.AddCronJobs(commandsAssembly);
        });
        
        // ASP.NET Core hosting
        services.AddQuartzHostedService(options =>
        {
            options.AwaitApplicationStarted = true;
            
            // when shutting down we want jobs to complete gracefully
            options.WaitForJobsToComplete = true;
        });
    }
}
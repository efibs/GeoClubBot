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
using UseCases.InputPorts.Club;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.InputPorts.DailyChallenge;
using UseCases.InputPorts.Excuses;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.InputPorts.Organization;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.DailyChallenge;
using UseCases.UseCases.Excuses;
using UseCases.UseCases.GeoGuessrAccountLinking;
using UseCases.UseCases.Organization;
using UseCases.UseCases.Strikes;
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
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers,
            AlwaysDownloadUsers = true
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
        services.AddHostedService<InitialSyncService>();

        // Add the output adapters 
        services.AddTransient<IGeoGuessrAccess, HttpGeoGuessrAccess>();
        services.AddTransient<IClubRepository, EfClubRepository>();
        services.AddTransient<IClubMemberRepository, EfClubMemberRepository>();
        services.AddTransient<IHistoryRepository, EfHistoryRepository>();
        services.AddTransient<IExcusesRepository, EfExcusesRepository>();
        services.AddTransient<IStrikesRepository, EfStrikesRepository>();
        services.AddTransient<IClubChallengeRepository, EfClubChallengeRepository>();
        services.AddTransient<IGeoGuessrUserRepository, EfGeoGuessrUserRepository>();
        services.AddTransient<IAccountLinkingRequestRepository, EfAccountLinkingRequestRepository>();
        services.AddTransient<IActivityStatusMessageSender, DiscordActivityStatusMessageSender>();
        services.AddTransient<IStatusUpdater, DiscordStatusUpdater>();
        services.AddTransient<IMessageSender, DiscordMessageSender>();
        services.AddTransient<IServerRolesAccess, DiscordServerRolesAccess>();
        services.AddTransient<IClubEventNotifier, SignalRClubEventNotifier>();
        services.AddTransient<IClubEventNotifier, DiscordMessageClubEventNotifier>();

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
        services.AddTransient<IReadAllStrikesUseCase, ReadAllStrikesUseCase>();
        services.AddTransient<ISetClubLevelStatusUseCase, SetClubLevelStatusUseCase>();
        services.AddTransient<IDistributeDailyChallengeRolesUseCase, DistributeDailyChallengeRolesUseCase>();
        services.AddTransient<IPlayerStatisticsUseCase, PlayerStatisticsUseCase>();
        services.AddTransient<IClubStatisticsUseCase, ClubStatisticsUseCase>();
        services.AddTransient<ISaveClubMembersUseCase, SaveClubMembersUseCase>();
        services.AddTransient<IGetLinkedDiscordUserIdUseCase, GetLinkedDiscordUserIdUseCase>();
        services.AddTransient<IStartAccountLinkingProcessUseCase, StartAccountLinkingUseCase>();
        services.AddTransient<ICompleteAccountLinkingUseCase, CompleteAccountLinkingUseCase>();
        services.AddTransient<IReadOrSyncGeoGuessrUserUseCase, ReadOrSyncGeoGuessrUserUseCase>();
        services.AddTransient<IUnlinkAccountsUseCase, UnlinkAccountsUseCase>();
        services.AddTransient<ISyncClubMemberRoleUseCase, SyncClubMemberRoleUseCase>();
        
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
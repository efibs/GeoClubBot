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
using Infrastructure.OutputAdapters.GeoGuessr;
using Microsoft.EntityFrameworkCore;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts.Club;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.DailyChallenge;
using UseCases.InputPorts.Excuses;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.InputPorts.Organization;
using UseCases.InputPorts.SelfRoles;
using UseCases.InputPorts.Strikes;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.ClubMembers;
using UseCases.UseCases.DailyChallenge;
using UseCases.UseCases.Excuses;
using UseCases.UseCases.GeoGuessrAccountLinking;
using UseCases.UseCases.MemberPrivateChannels;
using UseCases.UseCases.Organization;
using UseCases.UseCases.SelfRoles;
using UseCases.UseCases.Strikes;
using UseCases.UseCases.Users;
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

        // Add the GeoGuessr access along with it's http client
        services.AddHttpClient<IGeoGuessrAccess, HttpGeoGuessrAccess>(client =>
            {
                // Set the base address
                client.BaseAddress = new Uri("https://www.geoguessr.com/api/");

                // Set the token in the cookies
                client.DefaultRequestHeaders.Add("Cookie", $"_ncfa={geoGuessrToken}");
            })
            .AddResilienceHandler("GeoGuessrApiResiliencePipeline", p => ResiliencePipelines.AddGeoGuessrApiResiliencePipeline(p));

        // Add auxiliary services
        services.AddSingleton<DiscordBotReadyService>();
        
        // Add the input adapters
        services.AddHostedService<InitialSyncService>();
        services.AddHostedService<UserJoinedService>();
        services.AddHostedService<UpdateSelfRolesMessageService>();

        // Add the output adapters 
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
        services.AddTransient<IMessageAccess, DiscordMessageAccess>();
        services.AddTransient<IServerRolesAccess, DiscordServerRolesAccess>();
        services.AddTransient<ITextChannelAccess, DiscordTextChannelAccess>();
        services.AddTransient<IClubEventNotifier, SignalRClubEventNotifier>();
        services.AddTransient<IClubEventNotifier, DiscordMessageClubEventNotifier>();
        services.AddTransient<ISelfUserAccess, DiscordSelfUserAccess>();

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
        services.AddTransient<ISyncClubUseCase, SyncClubUseCase>();
        services.AddTransient<IClubMemberActivityRewardUseCase, ClubMemberActivityRewardUseCase>();
        services.AddTransient<IGeoGuessrUserIdsToDiscordUserIdsUseCase, GeoGuessrUserIdsToDiscordUserIdsUseCase>();
        services.AddTransient<ICancelAccountLinkingUseCase, CancelAccountLinkingUseCase>();
        services.AddTransient<IGetLinkedGeoGuessrUserUseCase, GetLinkedGeoGuessrUserUseCase>();
        services.AddTransient<IReadAllRelevantStrikesUseCase, ReadAllRelevantStrikesUseCase>();
        services.AddTransient<ICreateOrUpdateUserUseCase, CreateOrUpdateUserUseCase>();
        services.AddTransient<ICreateOrUpdateClubMemberUseCase, CreateOrUpdateClubMemberUseCase>();
        services.AddTransient<ICreateMemberPrivateChannelUseCase, CreateMemberPrivateChannelUseCase>();
        services.AddTransient<IDeleteMemberPrivateChannelUseCase, DeleteMemberPrivateChannelUseCase>();
        services.AddTransient<IRenderHistoryUseCase, RenderHistoryUseCase>();
        services.AddTransient<IRenderPlayerActivityUseCase, RenderPlayerActivityUseCase>();
        services.AddTransient<IUpdateSelfRolesMessageUseCase, UpdateSelfRolesMessageUseCase>();
        services.AddTransient<IUpdateSelfRolesMessageUseCase, UpdateSelfRolesMessageUseCase>();
        
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
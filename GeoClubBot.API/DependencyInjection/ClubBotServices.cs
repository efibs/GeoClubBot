using Configuration;
using Constants;
using GeoClubBot.Discord.OutputAdapters;
using Infrastructure.InputAdapters;
using Infrastructure.InputAdapters.Jobs;
using Infrastructure.OutputAdapters;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using Quartz;
using QuartzExtensions;
using Refit;
using UseCases.InputPorts.Club;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.DailyChallenge;
using UseCases.InputPorts.DailyMissionReminder;
using UseCases.InputPorts.Excuses;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.InputPorts.Organization;
using UseCases.InputPorts.SelfRoles;
using UseCases.InputPorts.Strikes;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.ClubMembers;
using UseCases.UseCases.DailyChallenge;
using UseCases.UseCases.DailyMissionReminder;
using UseCases.UseCases.Excuses;
using UseCases.UseCases.GeoGuessrAccountLinking;
using UseCases.UseCases.MemberPrivateChannels;
using UseCases.UseCases.Organization;
using UseCases.UseCases.SelfRoles;
using UseCases.UseCases.Strikes;
using UseCases.UseCases.Users;

namespace GeoClubBot.DependencyInjection;

/// <summary>
/// Helper class to register all required services in the dependency injection
/// </summary>
public static class ClubBotServices
{
    public static void AddClubBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Get the GeoGuessr configuration
        var geoGuessrConfig = new GeoGuessrConfiguration
        {
            SyncSchedule = null!,
            Clubs = null!
        };
        configuration.GetSection(GeoGuessrConfiguration.SectionName).Bind(geoGuessrConfig);

        // Register a named HttpClient per club with its own NCFA token
        foreach (var club in geoGuessrConfig.Clubs)
        {
            services.AddHttpClient($"GeoGuessr_{club.ClubId}", client =>
                {
                    client.BaseAddress = new Uri("https://www.geoguessr.com/api");
                    client.DefaultRequestHeaders.Add("Cookie", $"_ncfa={club.NcfaToken}");
                })
                .AddResilienceHandler($"GeoGuessrApiResiliencePipeline_{club.ClubId}",
                    ResiliencePipelines.AddGeoGuessrApiResiliencePipeline);
        }

        // Register the client factory
        services.AddSingleton<IGeoGuessrClientFactory, GeoGuessrClientFactory>();

        // Register a default IGeoGuessrClient using the main club's token (for club-independent operations)
        services.AddRefitClient<IGeoGuessrClient>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://www.geoguessr.com/api");
                client.DefaultRequestHeaders.Add("Cookie", $"_ncfa={geoGuessrConfig.MainClub.NcfaToken}");
            })
            .AddResilienceHandler("GeoGuessrApiResiliencePipeline", ResiliencePipelines.AddGeoGuessrApiResiliencePipeline);

        // Add the input adapters
        services.AddHostedService<InitialSyncService>();
        services.AddHostedService<UserJoinedService>();

        // Add the output adapters 
        services.AddTransient<IUnitOfWork, DbUnitOfWork>();
        services.AddTransient<IActivityStatusMessageSender, DiscordActivityStatusMessageSender>();
        services.AddTransient<IDiscordStatusUpdater, DiscordDiscordStatusUpdater>();
        services.AddTransient<IDiscordMessageAccess, DiscordDiscordMessageAccess>();
        services.AddTransient<IDiscordServerRolesAccess, DiscordDiscordServerRolesAccess>();
        services.AddTransient<IDiscordTextChannelAccess, DiscordDiscordTextChannelAccess>();
        services.AddTransient<IClubEventNotifier, SignalRClubEventNotifier>();
        services.AddTransient<IClubEventNotifier, DiscordMessageClubEventNotifier>();
        services.AddTransient<IDiscordSelfUserAccess, DiscordDiscordSelfUserAccess>();
        services.AddTransient<IDiscordDirectMessageAccess, DiscordDirectMessageAccess>();

        // Add the use cases
        services.AddTransient<ICheckGeoGuessrPlayerActivityUseCase, CheckGeoGuessrPlayerActivityUseCase>();
        services.AddTransient<ICalculateAverageXpUseCase, CalculateAverageXpUseCase>();
        services.AddTransient<IReadMemberStrikesUseCase, ReadMemberStrikesUseCase>();
        services.AddTransient<IAddExcuseUseCase, AddExcuseUseCase>();
        services.AddTransient<IUpdateExcuseUseCase, UpdateExcuseUseCase>();
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
        services.AddTransient<ISetDailyMissionReminderUseCase, SetDailyMissionReminderUseCase>();
        services.AddTransient<IStopDailyMissionReminderUseCase, StopDailyMissionReminderUseCase>();
        services.AddTransient<IGetDailyMissionReminderStatusUseCase, GetDailyMissionReminderStatusUseCase>();
        services.AddTransient<ISendDueRemindersUseCase, SendDueRemindersUseCase>();
        services.AddTransient<IGetClubTodaysXpUseCase, GetClubTodaysXpUseCase>();
        services.AddTransient<IGetActivityLeaderboardUseCase, GetActivityLeaderboardUseCase>();
        services.AddTransient<IGetClubByNameOrDefaultUseCase, GetClubByNameOrDefaultUseCase>();

        // Add the ai services
        services.AddAiServicesIfConfigured(configuration);
        
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
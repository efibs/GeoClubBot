using UseCases.InputPorts.Club;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.InputPorts.Organization;
using UseCases.InputPorts.Users;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.ClubMembers;
using UseCases.UseCases.MemberPrivateChannels;
using UseCases.UseCases.Organization;
using UseCases.UseCases.Users;

namespace GeoClubBot.DependencyInjection.Modules;

public static class ClubMembersModule
{
    public static IServiceCollection AddClubMembersModule(this IServiceCollection services)
    {
        // Club
        services.AddSingleton<ICheckClubLevelUseCase, CheckClubLevelUseCase>();
        services.AddTransient<ISetClubLevelStatusUseCase, SetClubLevelStatusUseCase>();
        services.AddTransient<ISyncClubsUseCase, SyncClubsUseCase>();
        services.AddTransient<IGetClubByNameOrDefaultUseCase, GetClubByNameOrDefaultUseCase>();

        // Club members
        services.AddTransient<IReadOrSyncClubMemberUseCase, ReadOrSyncClubMemberUseCase>();
        services.AddTransient<ISaveClubMembersUseCase, SaveClubMembersUseCase>();
        services.AddTransient<ICreateOrUpdateClubMemberUseCase, CreateOrUpdateClubMemberUseCase>();

        // Activity
        services.AddTransient<ICheckGeoGuessrPlayerActivityUseCase, CheckGeoGuessrPlayerActivityUseCase>();
        services.AddTransient<IGetActivityThisWeekUseCase, GetActivityThisWeekUseCase>();
        services.AddTransient<ICalculateAverageXpUseCase, CalculateAverageXpUseCase>();
        services.AddTransient<IGetLastCheckTimeUseCase, GetLastCheckTimeUseCase>();
        services.AddTransient<IClubMemberActivityRewardUseCase, ClubMemberActivityRewardUseCase>();
        services.AddTransient<IRenderPlayerActivityUseCase, RenderPlayerActivityUseCase>();
        services.AddTransient<IGetActivityLeaderboardUseCase, GetActivityLeaderboardUseCase>();
        services.AddTransient<IGetClubTodaysXpUseCase, GetClubTodaysXpUseCase>();
        services.AddTransient<IClubStatisticsUseCase, ClubStatisticsUseCase>();
        services.AddTransient<IPlayerStatisticsUseCase, PlayerStatisticsUseCase>();

        // Users
        services.AddTransient<ICreateOrUpdateUserUseCase, CreateOrUpdateUserUseCase>();
        services.AddTransient<IGeoGuessrUserIdsToDiscordUserIdsUseCase, GeoGuessrUserIdsToDiscordUserIdsUseCase>();

        // Member private channels
        services.AddTransient<ICreateMemberPrivateChannelUseCase, CreateMemberPrivateChannelUseCase>();
        services.AddTransient<IDeleteMemberPrivateChannelUseCase, DeleteMemberPrivateChannelUseCase>();

        // Organization
        services.AddTransient<ICleanupUseCase, CleanupUseCase>();

        return services;
    }
}

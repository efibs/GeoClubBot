using UseCases.InputPorts.Club;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.InputPorts.Organization;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.MemberPrivateChannels;
using UseCases.UseCases.Organization;

namespace GeoClubBot.DependencyInjection.Modules;

public static class ClubMembersModule
{
    public static IServiceCollection AddClubMembersModule(this IServiceCollection services)
    {
        // ClubMember and User use cases have migrated to MediatR; their handlers are
        // auto-discovered from the use-cases assembly in Program.cs.

        // Club (not yet migrated to MediatR)
        services.AddSingleton<ICheckClubLevelUseCase, CheckClubLevelUseCase>();
        services.AddTransient<ISetClubLevelStatusUseCase, SetClubLevelStatusUseCase>();
        services.AddTransient<ISyncClubsUseCase, SyncClubsUseCase>();
        services.AddTransient<IGetClubByNameOrDefaultUseCase, GetClubByNameOrDefaultUseCase>();

        // Activity (not yet migrated to MediatR)
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

        // Member private channels (not yet migrated to MediatR)
        services.AddTransient<ICreateMemberPrivateChannelUseCase, CreateMemberPrivateChannelUseCase>();
        services.AddTransient<IDeleteMemberPrivateChannelUseCase, DeleteMemberPrivateChannelUseCase>();

        // Organization (not yet migrated to MediatR)
        services.AddTransient<ICleanupUseCase, CleanupUseCase>();

        return services;
    }
}

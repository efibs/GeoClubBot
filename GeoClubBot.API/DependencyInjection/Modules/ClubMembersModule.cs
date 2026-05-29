using UseCases.UseCases.Club;
using UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;

namespace GeoClubBot.DependencyInjection.Modules;

public static class ClubMembersModule
{
    public static IServiceCollection AddClubMembersModule(this IServiceCollection services)
    {
        // ClubMember, User, Club, ClubMemberActivity, MemberPrivateChannels, and Organization
        // use cases have all migrated to MediatR; their handlers are auto-discovered from
        // the use-cases assembly in Program.cs.

        // IClubLevelTracker holds the in-memory last-observed level state shared across
        // CheckClubLevelCommand invocations, so it must be a singleton.
        services.AddSingleton<IClubLevelTracker, ClubLevelTracker>();

        // CheckGeoGuessrPlayerActivityHandler delegates work to three step classes; they
        // are not MediatR handlers, so they need explicit DI registration.
        services.AddTransient<ActivityCheckSyncStep>();
        services.AddTransient<ActivityStatusCalculator>();
        services.AddTransient<ActivityAverageXpRollupStep>();

        return services;
    }
}

namespace GeoClubBot.DependencyInjection.Modules;

public static class DailyChallengesModule
{
    public static IServiceCollection AddDailyChallengesModule(this IServiceCollection services)
    {
        // Daily-challenge, daily-mission-reminder, and daily-mission-logging handlers
        // are auto-registered via MediatR's assembly scan in Program.cs.
        return services;
    }
}

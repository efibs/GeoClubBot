using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Configuration;

public static class DependencyInjectionExtensions
{
    public static void AddClubBotOptions(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<DiscordConfiguration>()
            .Bind(config.GetSection(DiscordConfiguration.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddOptions<ClubLevelCheckerConfiguration>()
            .Bind(config.GetSection(ClubLevelCheckerConfiguration.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddOptions<ActivityCheckerConfiguration>()
            .Bind(config.GetSection(ActivityCheckerConfiguration.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddOptions<ActivityRewardConfiguration>()
            .Bind(config.GetSection(ActivityRewardConfiguration.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        
        services.AddOptions<DailyChallengesConfiguration>()
            .Bind(config.GetSection(DailyChallengesConfiguration.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DailyMissionReminderConfiguration>()
            .Bind(config.GetSection(DailyMissionReminderConfiguration.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<GeoGuessrConfiguration>()
            .Bind(config.GetSection(GeoGuessrConfiguration.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
using UseCases.InputPorts.DailyChallenge;
using UseCases.InputPorts.DailyMissionLogging;
using UseCases.InputPorts.DailyMissionReminder;
using UseCases.UseCases.DailyChallenge;
using UseCases.UseCases.DailyMissionLogging;
using UseCases.UseCases.DailyMissionReminder;

namespace GeoClubBot.DependencyInjection.Modules;

public static class DailyChallengesModule
{
    public static IServiceCollection AddDailyChallengesModule(this IServiceCollection services)
    {
        services.AddTransient<IDailyChallengeUseCase, DailyChallengeUseCase>();
        services.AddTransient<IDistributeDailyChallengeRolesUseCase, DistributeDailyChallengeRolesUseCase>();

        services.AddTransient<ISetDailyMissionReminderUseCase, SetDailyMissionReminderUseCase>();
        services.AddTransient<IStopDailyMissionReminderUseCase, StopDailyMissionReminderUseCase>();
        services.AddTransient<IGetDailyMissionReminderStatusUseCase, GetDailyMissionReminderStatusUseCase>();
        services.AddTransient<ISendDueRemindersUseCase, SendDueRemindersUseCase>();

        services.AddTransient<ILogDailyMissionsUseCase, LogDailyMissionsUseCase>();

        return services;
    }
}

using GeoClubBot.Discord.OutputAdapters;
using Infrastructure.InputAdapters;
using Infrastructure.OutputAdapters;
using UseCases.OutputPorts.Notifications;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Rendering;

namespace GeoClubBot.DependencyInjection.Modules;

public static class DiscordAdaptersModule
{
    public static IServiceCollection AddDiscordAdaptersModule(this IServiceCollection services)
    {
        services.AddHostedService<InitialSyncService>();
        services.AddHostedService<UserJoinedService>();
        services.AddHostedService<UserLeftService>();

        services.AddTransient<IDailyMissionRenderer, DiscordDailyMissionRenderer>();
        services.AddTransient<IActivityStatusMessageFormatter, DiscordActivityStatusMessageFormatter>();
        services.AddTransient<IActivityStatusMessageSender, DiscordActivityStatusMessageSender>();
        services.AddTransient<IDiscordStatusUpdater, DiscordDiscordStatusUpdater>();
        services.AddTransient<IDiscordMessageAccess, DiscordDiscordMessageAccess>();
        services.AddTransient<IDiscordServerRolesAccess, DiscordDiscordServerRolesAccess>();
        services.AddTransient<IDiscordTextChannelAccess, DiscordDiscordTextChannelAccess>();
        services.AddTransient<IDiscordSelfUserAccess, DiscordDiscordSelfUserAccess>();
        services.AddTransient<IDiscordDirectMessageAccess, DiscordDirectMessageAccess>();

        // Fan-out: every IClubEventNotifier consumer must inject IEnumerable<IClubEventNotifier>
        services.AddTransient<IClubEventNotifier, SignalRClubEventNotifier>();
        services.AddTransient<IClubEventNotifier, DiscordMessageClubEventNotifier>();

        return services;
    }
}

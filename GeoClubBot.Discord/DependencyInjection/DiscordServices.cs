using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GeoClubBot.Discord.Logging;
using GeoClubBot.Discord.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeoClubBot.Discord.DependencyInjection;

public static class DiscordServices
{
    public static void AddDiscordServices(this IServiceCollection services)
    {
        // Discord bot service must be the first service that starts
        services.AddHostedService<DiscordBotService>();

        // Add the discord socket client
        services.AddSingleton<DiscordSocketClient>(_ => new DiscordSocketClient(new DiscordSocketConfig
        {
            // AllUnprivileged includes GuildScheduledEvents and GuildInvites, which
            // we don't listen to. Excluding them avoids Discord.Net's unused-intent warnings.
            GatewayIntents = (GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers)
                & ~GatewayIntents.GuildScheduledEvents
                & ~GatewayIntents.GuildInvites,
            AlwaysDownloadUsers = true
        }));

        // Add the logging service and instantiate it immediately and 
        // therefore registering the logging callbacks.
        services.AddActivatedSingleton<DiscordLoggingService>();

        // Add services
        services.AddSingleton<DiscordBotReadyService>();
        services.AddHostedService<UpdateSelfRolesMessageService>();
        services.AddSingleton(p => new InteractionService(p.GetRequiredService<DiscordSocketClient>()));
        services.AddActivatedSingleton<InteractionHandler>();

        // Optional Discord channel log sink. The provider plugs into the logging pipeline; the
        // hosted processor drains the queue and delivers embeds. Both are no-ops while the sink is
        // disabled (no ChannelId configured), so they are safe to register unconditionally.
        services.AddSingleton<DiscordChannelLogQueue>();
        services.AddSingleton<DiscordLogEmbedFormatter>();
        services.AddSingleton<ILoggerProvider, DiscordChannelLoggerProvider>();
        services.AddHostedService<DiscordChannelLogProcessor>();
    }
}

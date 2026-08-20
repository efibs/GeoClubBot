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
    /// <param name="enableMessageContent">
    /// Requests the privileged MessageContent intent, needed to read the text of messages that do not
    /// mention the bot — without it a reply arrives with empty content and follow-up questions cannot
    /// work. Gated so a deployment with AI switched off never asks for a privileged intent it does
    /// not use. The intent must also be enabled in the Discord developer portal; requesting it
    /// without that takes the whole gateway connection down with close code 4014, not just the AI
    /// feature.
    /// </param>
    public static void AddDiscordServices(this IServiceCollection services, bool enableMessageContent = false)
    {
        // Discord bot service must be the first service that starts
        services.AddHostedService<DiscordBotService>();

        // Add the discord socket client
        services.AddSingleton<DiscordSocketClient>(_ => new DiscordSocketClient(new DiscordSocketConfig
        {
            // AllUnprivileged includes GuildScheduledEvents and GuildInvites, which
            // we don't listen to. Excluding them avoids Discord.Net's unused-intent warnings.
            GatewayIntents = ((GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers)
                    & ~GatewayIntents.GuildScheduledEvents
                    & ~GatewayIntents.GuildInvites)
                | (enableMessageContent ? GatewayIntents.MessageContent : GatewayIntents.None),
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

        // Entry point ("Launch") command: Discord.Net has no support for this command type, so these
        // two services talk to Discord's REST/Gateway APIs directly to stop it from posting a
        // channel message on every click. See EntryPointCommandGatewayListener for why.
        //
        // AddHostedService<T>() is deliberately NOT used here: when T is also registered via
        // AddHttpClient<T>(), AddHostedService<T>() constructs a second T directly from its
        // ImplementationType, bypassing the typed-client factory - it ends up with
        // Microsoft.Extensions.Http's default *unnamed* HttpClient (no BaseAddress) instead of the
        // one configured below. Routing through GetRequiredService<T>() reuses the correctly-wired
        // instance instead.
        services.AddHttpClient<EntryPointCommandConfigurationService>(client =>
        {
            client.BaseAddress = new Uri("https://discord.com/api/v10/");
        });
        services.AddHostedService(p => p.GetRequiredService<EntryPointCommandConfigurationService>());

        services.AddHttpClient<EntryPointCommandGatewayListener>(client =>
        {
            client.BaseAddress = new Uri("https://discord.com/api/v10/");
        });
        services.AddHostedService(p => p.GetRequiredService<EntryPointCommandGatewayListener>());

        // Optional Discord channel log sink. The provider plugs into the logging pipeline; the
        // hosted processor drains the queue and delivers embeds. Both are no-ops while the sink is
        // disabled (no ChannelId configured), so they are safe to register unconditionally.
        services.AddSingleton<DiscordChannelLogQueue>();
        services.AddSingleton<DiscordLogEmbedFormatter>();
        services.AddSingleton<ILoggerProvider, DiscordChannelLoggerProvider>();
        services.AddHostedService<DiscordChannelLogProcessor>();
    }
}

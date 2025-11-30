using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GeoClubBot.Discord.Services;
using Microsoft.Extensions.DependencyInjection;

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
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers,
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
    }
}
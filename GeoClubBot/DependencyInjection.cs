using Discord.Commands;
using Discord.WebSocket;
using GeoClubBot.Services;

namespace GeoClubBot;

/// <summary>
/// Helper class to register all required services in the dependency injection
/// </summary>
public static class DependencyInjection
{
    public static void AddClubBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Add the discord socket client
        services.AddSingleton<DiscordSocketClient>();
        
        // Add the discord command service
        services.AddSingleton<CommandService>();
        
        // Add the logging service and instantiate it immediately and 
        // therefore registering the logging callbacks.
        services.AddActivatedSingleton<DiscordLoggingService>();
        
        // Add the discord bot service
        services.AddHostedService<DiscordBotService>();
    }
}
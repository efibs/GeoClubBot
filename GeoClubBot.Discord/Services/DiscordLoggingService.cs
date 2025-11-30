using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace GeoClubBot.Discord.Services;

/// <summary>
/// Service providing logging functionality to the discord bot classes.
/// </summary>
public class DiscordLoggingService
{
    public DiscordLoggingService(DiscordSocketClient client, InteractionService interactionService,
        ILoggerFactory loggerFactory)
    {
        // Store the logger factory
        _loggerFactory = loggerFactory;

        // Attach the log method
        client.Ready += ReadyAsync;
        client.Log += LogAsync;
        interactionService.Log += LogAsync;
        
        // Attach connected / disconnected event listeners
        client.Connected += OnConnectedAsync;
        client.Disconnected += OnDisconnectedAsync;
    }

    private Task ReadyAsync()
    {
        // Create the logger
        var logger = _loggerFactory.CreateLogger("Startup");

        // Log startup message
        logger.LogInformation("Discord bot is ready.");

        return Task.CompletedTask;
    }

    private Task LogAsync(LogMessage message)
    {
        // Create the logger
        var logger = _loggerFactory.CreateLogger(message.Source);

        switch (message.Severity)
        {
            case LogSeverity.Critical:
                logger.LogCritical(message.Exception, message.Message);
                break;
            case LogSeverity.Error:
                logger.LogError(message.Exception, message.Message);
                break;
            case LogSeverity.Warning:
                logger.LogWarning(message.Exception, message.Message);
                break;
            case LogSeverity.Info:
                logger.LogInformation(message.Exception, message.Message);
                break;
            case LogSeverity.Verbose:
                logger.LogTrace(message.Exception, message.Message);
                break;
            case LogSeverity.Debug:
                logger.LogDebug(message.Exception, message.Message);
                break;
        }

        return Task.CompletedTask;
    }

    private Task OnConnectedAsync()
    {
        // Create the logger
        var logger = _loggerFactory.CreateLogger("Connection");
        
        logger.LogInformation("Connected to Discord Gateway.");
        return Task.CompletedTask;
    }
    
    private Task OnDisconnectedAsync(Exception ex)
    {
        // Create the logger
        var logger = _loggerFactory.CreateLogger("Connection");
        
        logger.LogWarning(ex, "Disconnected from the Discord Gateway.");
        return Task.CompletedTask;
    }
    
    private readonly ILoggerFactory _loggerFactory;
}
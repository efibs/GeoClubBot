using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace GeoClubBot.Services;

/// <summary>
/// Service providing logging functionality to the discord bot classes.
/// </summary>
public class DiscordLoggingService
{
    public DiscordLoggingService(DiscordSocketClient client, CommandService commandService, ILoggerFactory loggerFactory)
    {
        // Store the logger factory
        _loggerFactory = loggerFactory;
        
        // Attach the log method
        client.Ready += ReadyAsync;
        client.Log += LogAsync;
        commandService.Log += LogAsync;
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
    
    private readonly ILoggerFactory _loggerFactory;
}
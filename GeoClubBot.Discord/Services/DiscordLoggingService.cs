using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace GeoClubBot.Discord.Services;

/// <summary>
/// Service providing logging functionality to the discord bot classes.
/// </summary>
public partial class DiscordLoggingService
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
        LogDiscordBotReady(_loggerFactory.CreateLogger("Startup"));
        return Task.CompletedTask;
    }

    private Task LogAsync(LogMessage message)
    {
        var logger = _loggerFactory.CreateLogger(message.Source);

        // Discord periodically asks clients to reconnect for load-balancing/maintenance.
        // The library reconnects automatically, so this is routine rather than a real
        // problem. It surfaces as a warning carrying a GatewayReconnectException; downgrade
        // it to information to avoid noisy periodic warnings.
        if (message.Exception is GatewayReconnectException)
        {
            LogDiscordInfo(logger, message.Exception, message.Message);
            return Task.CompletedTask;
        }

        switch (message.Severity)
        {
            case LogSeverity.Critical:
                LogDiscordCritical(logger, message.Exception, message.Message);
                break;
            case LogSeverity.Error:
                LogDiscordError(logger, message.Exception, message.Message);
                break;
            case LogSeverity.Warning:
                LogDiscordWarning(logger, message.Exception, message.Message);
                break;
            case LogSeverity.Info:
                LogDiscordInfo(logger, message.Exception, message.Message);
                break;
            case LogSeverity.Verbose:
                LogDiscordTrace(logger, message.Exception, message.Message);
                break;
            case LogSeverity.Debug:
                LogDiscordDebug(logger, message.Exception, message.Message);
                break;
        }

        return Task.CompletedTask;
    }

    private Task OnConnectedAsync()
    {
        LogConnectedToGateway(_loggerFactory.CreateLogger("Connection"));
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(Exception ex)
    {
        var logger = _loggerFactory.CreateLogger("Connection");

        // A server-requested reconnect is expected and handled automatically by the
        // library, so log it as information. Other disconnects may indicate a real
        // problem and stay at warning.
        if (ex is GatewayReconnectException)
        {
            LogGatewayRequestedReconnect(logger);
        }
        else
        {
            LogDisconnectedFromGateway(logger, ex);
        }

        return Task.CompletedTask;
    }

    private readonly ILoggerFactory _loggerFactory;

    [LoggerMessage(LogLevel.Information, "Discord bot is ready.")]
    static partial void LogDiscordBotReady(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Connected to Discord Gateway.")]
    static partial void LogConnectedToGateway(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Disconnected from the Discord Gateway.")]
    static partial void LogDisconnectedFromGateway(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Information, "Disconnected from the Discord Gateway: server requested a reconnect.")]
    static partial void LogGatewayRequestedReconnect(ILogger logger);

    // The Discord library emits arbitrary message text at runtime; we forward it as a
    // structured "{Message}" property so the placeholder name is stable.
    [LoggerMessage(LogLevel.Critical, "{Message}")]
    static partial void LogDiscordCritical(ILogger logger, Exception? exception, string message);

    [LoggerMessage(LogLevel.Error, "{Message}")]
    static partial void LogDiscordError(ILogger logger, Exception? exception, string message);

    [LoggerMessage(LogLevel.Warning, "{Message}")]
    static partial void LogDiscordWarning(ILogger logger, Exception? exception, string message);

    [LoggerMessage(LogLevel.Information, "{Message}")]
    static partial void LogDiscordInfo(ILogger logger, Exception? exception, string message);

    [LoggerMessage(LogLevel.Trace, "{Message}")]
    static partial void LogDiscordTrace(ILogger logger, Exception? exception, string message);

    [LoggerMessage(LogLevel.Debug, "{Message}")]
    static partial void LogDiscordDebug(ILogger logger, Exception? exception, string message);
}

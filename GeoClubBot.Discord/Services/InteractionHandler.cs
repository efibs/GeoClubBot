using Configuration;
using Discord.Interactions;
using Discord.WebSocket;
using GeoClubBot.Discord.InputAdapters.Interactions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Discord.Services;

public partial class InteractionHandler
{
    public InteractionHandler(DiscordSocketClient client, InteractionService interactionService,
        IServiceProvider serviceProvider, ILogger<InteractionHandler> logger, IOptions<DiscordConfiguration> config)
    {
        _client = client;
        _interactionService = interactionService;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _config = config.Value;

        client.Ready += CreateSlashCommandsAsync;
        client.InteractionCreated += HandleInteractionAsync;
    }

    private async Task CreateSlashCommandsAsync()
    {
        try
        {
            // Get the assembly containing the commands
            var commandsAssembly = typeof(InteractionsAssemblyMarker).Assembly;

            // Add the modules via reflection
            await _interactionService.AddModulesAsync(commandsAssembly, _serviceProvider).ConfigureAwait(false);

            // Register the slash commands
            await _interactionService.RegisterCommandsToGuildAsync(_config.ServerId).ConfigureAwait(false);

            LogSlashCommandsRegistered(_logger);
        }
        catch (Exception ex)
        {
            LogFailedToCreateSlashCommands(_logger, ex);
        }
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        // Log debug
        LogHandlingInteractionOnGuildInChannel(interaction.GuildId, interaction.ChannelId);

        try
        {
            // Create the interaction context
            var ctx = new SocketInteractionContext(_client, interaction);

            // Execute the command
            var result = await _interactionService.ExecuteCommandAsync(ctx, _serviceProvider).ConfigureAwait(false);

            // If the execution failed
            if (!result.IsSuccess)
            {
                LogSlashCommandFailed(result.ErrorReason);
            }
        }
        catch (Exception ex)
        {
            LogFailedToHandleInteraction(_logger, ex);
        }
    }

    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InteractionHandler> _logger;
    private readonly DiscordConfiguration _config;
    
    [LoggerMessage(LogLevel.Debug, "Handling interaction on guild {guild} in channel {channel}")]
    partial void LogHandlingInteractionOnGuildInChannel(ulong? guild, ulong? channel);

    [LoggerMessage(LogLevel.Error, "Slash command failed: {reason}")]
    partial void LogSlashCommandFailed(string reason);

    [LoggerMessage(LogLevel.Information, "Slash commands registered")]
    static partial void LogSlashCommandsRegistered(ILogger<InteractionHandler> logger);

    [LoggerMessage(LogLevel.Error, "Failed to create slash commands")]
    static partial void LogFailedToCreateSlashCommands(ILogger<InteractionHandler> logger, Exception ex);

    [LoggerMessage(LogLevel.Error, "Failed to handle interaction")]
    static partial void LogFailedToHandleInteraction(ILogger<InteractionHandler> logger, Exception ex);
}
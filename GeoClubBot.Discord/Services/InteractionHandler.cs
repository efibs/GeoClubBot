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

        client.Ready += _createSlashCommandsAsync;
        client.InteractionCreated += _handleInteractionAsync;
    }

    private async Task _createSlashCommandsAsync()
    {
        try
        {
            // Get the assembly containing the commands
            var commandsAssembly = typeof(InteractionsAssemblyMarker).Assembly;

            // Add the modules via reflection
            await _interactionService.AddModulesAsync(commandsAssembly, _serviceProvider).ConfigureAwait(false);

            // Register the slash commands
            await _interactionService.RegisterCommandsToGuildAsync(_config.ServerId).ConfigureAwait(false);

            // Log information
            _logger.LogInformation("Slash commands registered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create slash commands");
        }
    }

    private async Task _handleInteractionAsync(SocketInteraction interaction)
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
            _logger.LogError(ex, "Failed to handle interaction");
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
}
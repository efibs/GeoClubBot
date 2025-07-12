using System.Reflection;
using Constants;
using Discord.Interactions;
using Discord.WebSocket;
using Infrastructure.InputAdapters.Commands;

namespace GeoClubBot.Services;

public class InteractionHandler
{
    public InteractionHandler(DiscordSocketClient client, InteractionService interactionService,
        IServiceProvider serviceProvider, ILogger<InteractionHandler> logger, IConfiguration config)
    {
        _client = client;
        _interactionService = interactionService;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _config = config;

        client.Ready += _createSlashCommandsAsync;
        client.InteractionCreated += HandleSlashCommandAsync;
    }

    private async Task _createSlashCommandsAsync()
    {
        // Get the assembly containing the commands
        var commandsAssembly = typeof(CommandAssemblyMarker).Assembly;

        // Add the modules via reflection
        await _interactionService.AddModulesAsync(commandsAssembly, _serviceProvider);

        // Register the slash commands
#if DEBUG
        await _interactionService.RegisterCommandsToGuildAsync(
            _config.GetValue<ulong>(ConfigKeys.ActivityCheckerMainServerIdConfigurationKey));
#else
        await _interactionService.RegisterCommandsGloballyAsync();
#endif

        // Log information
        _logger.LogInformation("Slash commands registered");
    }

    private async Task HandleSlashCommandAsync(SocketInteraction interaction)
    {
        // Log debug
        _logger.LogDebug("Handling interaction on guild {guild} in channel {channel}", interaction.GuildId,
            interaction.ChannelId);

        try
        {
            // Create the interaction context
            var ctx = new SocketInteractionContext(_client, interaction);

            // Execute the command
            var result = await _interactionService.ExecuteCommandAsync(ctx, _serviceProvider);

            // If the execution failed
            if (!result.IsSuccess)
            {
                _logger.LogError("Slash command failed: {reason}", result.ErrorReason);
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
    private readonly IConfiguration _config;
}
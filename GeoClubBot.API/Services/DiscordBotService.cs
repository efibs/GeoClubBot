using System.Text;
using Constants;
using Discord;
using Discord.WebSocket;
using Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;

namespace GeoClubBot.Services;

/// <summary>
/// Class managing the discord socket
/// </summary>
/// <param name="client">The discord socket client</param>
public class DiscordBotService : IHostedService
{
    public DiscordBotService(DiscordSocketClient client, IConfiguration config, ILogger<DiscordBotService> logger,
        ILoggerFactory loggerFactory)
    {
        _client = client;
        _config = config;
        _logger = logger;

        var vllmEndpoint = "http://localhost:8000/v1";
        var modelName = "openai/gpt-oss-20b";
        var apiKey = "no-key";

        var kernelBuilder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: modelName, apiKey: apiKey, endpoint: new Uri(vllmEndpoint));
        kernelBuilder.Plugins.AddFromObject(new PlonkItPlugin(loggerFactory.CreateLogger<PlonkItPlugin>()),
            "ReadPlonkItGuide");
        _kernel = kernelBuilder.Build();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Get the token from the configuration
        var token = _config.GetValue<string>(ConfigKeys.DiscordBotTokenConfigurationKey);

        // If the token was not given
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Discord Bot token not set.");
        }

        // Login the bot
        await _client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);

        // Start the bot
        await _client.StartAsync().ConfigureAwait(false);

        _client.MessageReceived += _onMessageReceived;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do here
        return Task.CompletedTask;
    }

    private async Task _onMessageReceived(SocketMessage socketMessage)
    {
        if (socketMessage.MentionedUserIds.Contains(_client.CurrentUser.Id) == false)
        {
            return;
        }

        Task.Run(async () => await _handleMessageAsync(socketMessage).ConfigureAwait(false));
    }

    private async Task _handleMessageAsync(SocketMessage socketMessage)
    {
        try
        {
            _logger.LogDebug($"Handling message using AI: {socketMessage.Content}");

            // Get the message
            var message = socketMessage.Content.Replace($"<@{_client.CurrentUser.Id}>", "@DRAGON");
            ChatHistory history = [];
            history.AddSystemMessage(
                "You are **DRAGON**, a helpful GeoGuessr assistant Discord bot. " +
                "Users will mention you with **@DRAGON**. " +

                "### Formatting Rules\n" +
                "- You may use **simplified Markdown syntax** for your responses.\n" +
                "- Allowed: headings, bullet lists, numbered lists, **bold**, *italic*, __underline__, subtext, masked links, code blocks, and block quotes.\n" +
                "- Forbidden: tables, horizontal dividers, or any attempt to imitate tables using symbols or spacing.\n" +

                "### Domain Knowledge\n" +
                "In GeoGuessr, a **meta** originally referred to identifying information visible only in Google Street View imagery — such as the car color used for image capture — that is not observable in real life. " +
                "Today, however, players often use the term *meta* more broadly to describe any clue or pattern that helps identify a location.\n" +

                "### Resources\n" +
                "You have access to the **ReadPlonkItGuide**, a trusted source of GeoGuessr metas for specific countries and territories from the PlonkIt website.\n" +
                "- If a user asks about *meta-related* topics, **consult the PlonkIt guide first**.\n" +
                "- The guide includes most countries and some territories (e.g., *Christmas Island*, which belongs to Australia).\n" +
                "- If a specific territory does not have its own page, check the corresponding country’s page instead — these often include relevant information.\n" +

                "### Source Attribution\n" +
                "Always cite your sources as **clickable links** (masked Markdown links)."
            );
            history.AddUserMessage(message);

            OpenAIPromptExecutionSettings promtExecutionSettings = new()
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0.2
            };
            
            var chatSvc = _kernel.GetRequiredService<IChatCompletionService>();
            await socketMessage.Channel.TriggerTypingAsync().ConfigureAwait(false);
            var response = await chatSvc.GetChatMessageContentAsync(history, promtExecutionSettings, _kernel)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogError("LLM failed to respond.");
                return;
            }

            // For every split
            foreach (var substring in response.Content.SplitAtCharWithLimit("\n", 2000))
            {
                await socketMessage.Channel.SendMessageAsync(substring).ConfigureAwait(false);
            }

            _logger.LogDebug($"Handling done.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI response");
        }
    }

    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly Kernel _kernel;
    private readonly ILogger<DiscordBotService> _logger;
}
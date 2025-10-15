using System.Text;
using Constants;
using Discord;
using Discord.WebSocket;
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
    public DiscordBotService(DiscordSocketClient client, IConfiguration config, ILogger<DiscordBotService> logger, ILoggerFactory loggerFactory)
    {
        _client = client;
        _config = config;
        _logger = logger;
        
        var vllmEndpoint = "http://localhost:8000/v1";
        var modelName = "openai/gpt-oss-20b";
        var apiKey = "no-key";
        
        var kernelBuilder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: modelName, apiKey: apiKey, endpoint: new Uri(vllmEndpoint));
        kernelBuilder.Plugins.AddFromObject(new PlonkItPlugin(loggerFactory.CreateLogger<PlonkItPlugin>()), "ReadPlonkItGuide");
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
            history.AddSystemMessage("You are DRAGON, a helpful GeoGuessr assistant Discord bot. The user will " +
                                     "mention you using \"@DRAGON\"." +
                                     "You can use the simplified markdown syntax to format your responses. " +
                                     "You can use headings, lists, bold font, italic font, underlined text, subtext, masked " +
                                     "links, code blocks and block quotes. Do not use tables. Do not use horizontal dividers. " +
                                     "Do not try to emulate tables with other symbols." +
                                     "Here are some explanations to what language the user will use: A meta originally was " +
                                     "an information usable to identify where in the world you are in Google Street view " +
                                     "but you don't have that information when you go there in real live. For example the " +
                                     "color of the car that was used to capture the Street View images is a meta. Nowadays " +
                                     "however the word meta will be used for basically everything you can use to identify " +
                                     "where you are." +
                                     "You can use the ReadPlonkItGuide to read GeoGuessr metas for specific " +
                                     "countries from a trustful website. If the user asks about anything meta related, " +
                                     "read the PlonkIt guide first. The website mostly contains countries and some " +
                                     "territories such as the christmas island, which is part of Australia. If the site " +
                                     "for a territory does not exist, try reading the page for the country this territory is in. " +
                                     "Oftentimes the page for the country also includes information for that territory. " +
                                     "Always state your sources as clickable links.");
            history.AddUserMessage(message);

            OpenAIPromptExecutionSettings promtExecutionSettings = new()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var counter = 0;
            var msgBuilder = new StringBuilder();
            var chatSvc = _kernel.GetRequiredService<IChatCompletionService>();
            await foreach (var response in chatSvc.GetStreamingChatMessageContentsAsync(
                               history,
                               executionSettings: promtExecutionSettings,
                               kernel: _kernel).ConfigureAwait(false))
            {

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    continue;
                }

                //_logger.LogDebug($"Response: {response.Content}");

                if (counter++ % 1000 == 0)
                {
                    await socketMessage.Channel.TriggerTypingAsync().ConfigureAwait(false);
                }

                if (msgBuilder.Length > 1500 && response.Content.Contains("\n"))
                {
                    var split = response.Content.Split("\n");
                    msgBuilder.Append(split[0]);
                    await socketMessage.Channel.SendMessageAsync(msgBuilder.ToString()).ConfigureAwait(false);
                    msgBuilder.Clear();
                    msgBuilder.Append(split[1]);
                }
                else
                {
                    msgBuilder.Append(response.Content);
                }
            }

            if (msgBuilder.Length > 0)
            {
                await socketMessage.Channel.SendMessageAsync(msgBuilder.ToString()).ConfigureAwait(false);
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
using System.Text;
using Constants;
using Discord;
using Discord.WebSocket;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI;

namespace GeoClubBot.Services;

/// <summary>
/// Class managing the discord socket
/// </summary>
/// <param name="client">The discord socket client</param>
public class DiscordBotService : IHostedService
{
    public DiscordBotService(DiscordSocketClient client, IConfiguration config)
    {
        _client = client;
        _config = config;
        
        var vllmEndpoint = "http://localhost:8000/v1";
        var modelName = "openai/gpt-oss-20b";
        var apiKey = "no-key";
        
        _kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: modelName, apiKey: apiKey, endpoint: new Uri(vllmEndpoint))
            .Build();
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

        await socketMessage.Channel.TriggerTypingAsync().ConfigureAwait(false);
        
        // Get the message
        var message = socketMessage.Content.Replace($"<@{_client.CurrentUser.Id}>", "@DRAGON");
        ChatHistory history = [];
        history.AddSystemMessage("You are DRAGON, a helpful GeoGuessr assistant Discord bot. You can use the simplified markdown syntax to format your responses. You can use headings, lists, bold font, italic font, underlined text, subtext, masked links, code blocks and block quotes. The user will mention you using \"@DRAGON\". Here are some explanations to what language the user will use: A meta originally was an information usable to identify where in the world you are in Google Street view but you don't have that information when you go there in real live. For example the color of the car that was used to capture the Street View images is a meta. Nowadays however the word meta will be used for basically everything you can use to identify where you are.");
        history.AddUserMessage(message);

        var msgBuilder = new StringBuilder();
        var chatSvc = _kernel.GetRequiredService<IChatCompletionService>();
        await foreach (var response in chatSvc.GetStreamingChatMessageContentsAsync(history).ConfigureAwait(false))
        {
            
            if (msgBuilder.Length > 1500 && response.Content!.Contains("\n"))
            {
                var split = response.Content.Split("\n");
                msgBuilder.Append(split[0]);
                await socketMessage.Channel.SendMessageAsync(msgBuilder.ToString()).ConfigureAwait(false);
                msgBuilder.Clear();
                await socketMessage.Channel.TriggerTypingAsync().ConfigureAwait(false);
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
    }

    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly Kernel _kernel;
}
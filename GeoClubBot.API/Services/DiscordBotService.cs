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
        var message = socketMessage.Content.Replace($"<@{_client.CurrentUser.Id}>", "DRAGON");
        ChatHistory history = [];
        history.AddSystemMessage("You are DRAGON, a helpful GeoGuessr assistant.");
        history.AddUserMessage(message);
        
        var chatSvc = _kernel.GetRequiredService<IChatCompletionService>();
        var aiResponse = await chatSvc.GetChatMessageContentAsync(history).ConfigureAwait(false);
        if (aiResponse.Content?.Length > 2000)
        {
            // Convert string to stream
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            await writer.WriteAsync(aiResponse.Content).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            stream.Position = 0;
            
            await socketMessage.Channel.SendFileAsync(stream, "response.md").ConfigureAwait(false);
        }
        else
        {
            await socketMessage.Channel.SendMessageAsync(aiResponse.Content).ConfigureAwait(false);
        }
        
    }

    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly Kernel _kernel;
}
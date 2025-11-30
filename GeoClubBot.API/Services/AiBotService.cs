using Discord;
using Discord.WebSocket;
using Extensions;
using GeoClubBot.Discord.Services;
using UseCases.InputPorts.AI;
using UseCases.UseCases.AI;

namespace GeoClubBot.Services;

public class AiBotService(PlonkItGuideVectorStore plonkItGuideVectorStore, DiscordBotReadyService botReadyService, DiscordSocketClient client, IGeoGuessrChatBotUseCase chatBotUseCase) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Initialize the qdrant storage in the background
        _ = Task.Run(plonkItGuideVectorStore.InitializeAsync, cancellationToken);
        
        // Wait for the bot to be ready
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);
        
        // Attach message handler
        client.MessageReceived += _onMessageReceived;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Detatch message handler
        client.MessageReceived -= _onMessageReceived;
        
        return Task.CompletedTask;
    }
    
    private Task _onMessageReceived(SocketMessage socketMessage)
    {
        if (!socketMessage.MentionedUserIds.Contains(client.CurrentUser.Id) || 
            socketMessage is not SocketUserMessage { ReferencedMessage: null } socketUserMessage)
        {
            return Task.CompletedTask;
        }

        Task.Run(async () => await _handleMessageAsync(socketUserMessage).ConfigureAwait(false));
        
        return Task.CompletedTask;
    }

    private async Task _handleMessageAsync(IUserMessage socketMessage)
    {
        // Get the ai response 
        var response = await chatBotUseCase
            .GetAiResponseAsync(socketMessage.Content,() => socketMessage.Channel.TriggerTypingAsync())
            .ConfigureAwait(false);
        
        // If there was no response
        if (response is null)
        {
            return;
        }
        
        var index = 0;
        // For every split
        foreach (var substring in response.SplitAtCharWithLimit("\n", 2000))
        {
            if (index++ == 0)
            {
                await socketMessage
                    .ReplyAsync(substring)
                    .ConfigureAwait(false);
            }
            else
            {
                await socketMessage.Channel
                    .SendMessageAsync(substring)
                    .ConfigureAwait(false);
            }
        }
    }

}
using Discord;
using Discord.WebSocket;
using Extensions;
using GeoClubBot.Discord.Services;
using MediatR;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI;

namespace GeoClubBot.Services;

public class AiBotService(
    IPlonkItGuideVectorStore plonkItGuideVectorStore,
    DiscordBotReadyService botReadyService,
    DiscordSocketClient client,
    ISender mediator) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Initialize the qdrant storage in the background
        _ = Task.Run(() => plonkItGuideVectorStore.InitializeAsync(cancellationToken), cancellationToken);

        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);

        client.MessageReceived += _onMessageReceived;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
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
        var response = await mediator
            .Send(new GetAiResponseQuery(socketMessage.Content, () => socketMessage.Channel.TriggerTypingAsync()))
            .ConfigureAwait(false);

        if (response is null)
        {
            return;
        }

        var index = 0;
        foreach (var substring in response.SplitAtCharWithLimit("\n", 2000))
        {
            if (index++ == 0)
            {
                await socketMessage.ReplyAsync(substring).ConfigureAwait(false);
            }
            else
            {
                await socketMessage.Channel.SendMessageAsync(substring).ConfigureAwait(false);
            }
        }
    }
}

using Configuration;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.AI.Conversations;

namespace GeoClubBot.Services;

/// <summary>
/// Posts an answer-shaped message into a channel: the clue of the day. Rendered and uploaded exactly
/// as an answer to a question is (<see cref="AiConversationGateway.PostAsync(IMessageChannel, IUserMessage?, AiAnswer, IImageRelay, AiImageRelayConfiguration, ILogger)"/>),
/// without a message to reply to.
/// </summary>
public sealed partial class DiscordAiChannelPublisher(
    DiscordSocketClient client,
    IImageRelay imageRelay,
    IOptions<AiImageRelayConfiguration> relayConfiguration,
    ILogger<DiscordAiChannelPublisher> logger) : IAiChannelPublisher
{
    public async Task<ulong?> PostAsync(ulong channelId, AiAnswer answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        // Cached by the gateway for a channel the bot can see; fetched through REST for one it cannot
        // yet, or when the cache has not caught up after a start-up.
        var channel = client.GetChannel(channelId) as IMessageChannel
                      ?? await client.Rest.GetChannelAsync(channelId).ConfigureAwait(false) as IMessageChannel;

        if (channel is null)
        {
            LogNoChannel(logger, channelId);
            return null;
        }

        try
        {
            var posted = await AiConversationGateway.PostAsync(channel, question: null, answer, imageRelay, relayConfiguration.Value, logger)
                .ConfigureAwait(false);

            return posted.LastMessageId == 0 ? null : posted.LastMessageId;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, channelId, exception);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Channel {ChannelId} is no text channel the bot can post to.")]
    static partial void LogNoChannel(ILogger logger, ulong channelId);

    [LoggerMessage(LogLevel.Warning, "Posting to channel {ChannelId} failed.")]
    static partial void LogFailed(ILogger logger, ulong channelId, Exception exception);
}

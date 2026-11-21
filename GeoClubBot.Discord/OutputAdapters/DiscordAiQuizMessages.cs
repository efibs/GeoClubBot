using Discord;
using Discord.WebSocket;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Discord.OutputAdapters;

/// <summary>Edits a quiz question's message to show its answer, for the reveal job (#491).</summary>
public sealed partial class DiscordAiQuizMessages(DiscordSocketClient client, ILogger<DiscordAiQuizMessages> logger) : IAiQuizMessages
{
    public async Task<bool> ShowAnswerAsync(AiQuizRound round, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(round);

        if (round.MessageId is not { } messageId)
        {
            return false;
        }

        try
        {
            var channel = client.GetChannel(round.ChannelId) as IMessageChannel
                          ?? await client.Rest.GetChannelAsync(round.ChannelId).ConfigureAwait(false) as IMessageChannel;
            if (channel is null || await channel.GetMessageAsync(messageId).ConfigureAwait(false) is not IUserMessage message)
            {
                return false;
            }

            await message.ModifyAsync(properties =>
            {
                properties.Content = AiQuizFormatter.Revealed(round);
                properties.Components = AiQuizFormatter.Buttons(round);
                properties.AllowedMentions = AllowedMentions.None;
            }).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, exception, round.Id);
            return false;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Could not show the answer of quiz round {RoundId}.")]
    static partial void LogFailed(ILogger logger, Exception exception, Guid roundId);
}

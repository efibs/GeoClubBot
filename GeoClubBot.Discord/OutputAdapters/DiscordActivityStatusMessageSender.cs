using Configuration;
using Discord.WebSocket;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts;

namespace GeoClubBot.Discord.OutputAdapters;

public sealed class DiscordActivityStatusMessageSender(
    DiscordSocketClient client,
    IActivityStatusMessageFormatter formatter,
    IOptions<DiscordConfiguration> discordConfig,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig) : IActivityStatusMessageSender
{
    private const int MaxNumPlayersPerMessage = 15;

    public async Task SendActivityStatusUpdateMessageAsync(
        List<ClubMemberActivityStatus> statuses, string clubName, int minXP, CancellationToken cancellationToken = default)
    {
        var channel = ResolveChannel();

        var playersWithFailedRequirement = statuses
            .Where(s => s.TargetAchieved == false)
            .OrderByDescending(s => s.NumStrikes)
            .ThenBy(s => s.XpSinceLastUpdate)
            .ToList();

        var firstChunk = playersWithFailedRequirement.Take(MaxNumPlayersPerMessage).ToList();
        await channel
            .SendMessageAsync(formatter.FormatStatusUpdateHeader(firstChunk, clubName, minXP))
            .ConfigureAwait(false);

        var skipCount = MaxNumPlayersPerMessage;
        while (playersWithFailedRequirement.Skip(skipCount).Any())
        {
            var chunk = playersWithFailedRequirement.Skip(skipCount).Take(MaxNumPlayersPerMessage).ToList();
            await channel.SendMessageAsync(formatter.FormatPlayerChunk(chunk)).ConfigureAwait(false);
            skipCount += MaxNumPlayersPerMessage;
        }

        var playersWithIndividualTargets = statuses
            .Where(s => !string.IsNullOrWhiteSpace(s.IndividualTargetReason))
            .ToList();

        if (playersWithIndividualTargets.Count > 0)
        {
            await channel
                .SendMessageAsync(formatter.FormatIndividualTargets(playersWithIndividualTargets))
                .ConfigureAwait(false);
        }
    }

    public async Task SendAverageXpMessageAsync(
        List<ClubMemberAverageXp> topMembers,
        List<ClubMemberAverageXp> bottomMembers,
        string clubName,
        int historyDepth,
        CancellationToken cancellationToken = default)
    {
        var message = formatter.FormatAverageXpSummary(topMembers, bottomMembers, historyDepth);
        if (message is null)
        {
            return;
        }

        var channel = ResolveChannel();
        await channel.SendMessageAsync(message).ConfigureAwait(false);
    }

    private SocketTextChannel ResolveChannel()
    {
        var server = client.GetGuild(discordConfig.Value.ServerId)
            ?? throw new InvalidOperationException($"No server found for id {discordConfig.Value.ServerId}");

        return server.GetTextChannel(activityCheckerConfig.Value.TextChannelId)
            ?? throw new InvalidOperationException($"No channel found for id {activityCheckerConfig.Value.TextChannelId}");
    }
}

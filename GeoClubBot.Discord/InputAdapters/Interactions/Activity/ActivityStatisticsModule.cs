using System.Globalization;
using System.Text;
using Discord.Interactions;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.ClubMemberActivity;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

public partial class ActivityModule
{
    public partial class ActivityStatisticsModule(
        ISender mediator,
        ILogger<ActivityStatisticsModule> logger) : ClubBotInteractionModule(mediator, logger)
    {
        [SlashCommand("player", "A member's activity statistics")]
        public async Task ReadPlayerStatisticsAsync(
            [Autocomplete(typeof(MemberAutocompleteHandler))][Summary(description: "The member, as suggested")] string member)
        {
            var result = await Mediator.Send(new PlayerStatisticsQuery(member)).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await RespondAsync(result.Error.Message, ephemeral: true).ConfigureAwait(false);
                return;
            }

            var stats = result.Value;

            var builder = new StringBuilder("# Activity statistics of player ");
            builder.AppendLine(stats.Nickname);
            builder.Append("History since: ");
            builder.AppendLine(stats.HistorySince.ToString("d"));
            builder.Append("Num history entries: ");
            builder.AppendLine(stats.NumHistoryEntries.ToString());
            builder.Append("Average points: ");
            builder.AppendLine(stats.AveragePoints.ToString(CultureInfo.InvariantCulture));
            builder.Append("Min points: ");
            builder.AppendLine(stats.MinPoints.ToString());
            builder.Append("1. Quartile points: ");
            builder.AppendLine(stats.FirstQuartilePoints.ToString());
            builder.Append("Median points: ");
            builder.AppendLine(stats.MedianPoints.ToString());
            builder.Append("3. Quartile points: ");
            builder.AppendLine(stats.ThirdQuartilePoints.ToString());
            builder.Append("Max points: ");
            builder.AppendLine(stats.MaxPoints.ToString());

            await RespondAsync(builder.ToString(), ephemeral: true).ConfigureAwait(false);
        }

        [SlashCommand("club", "The club's activity statistics")]
        public async Task ReadClubStatisticsAsync()
        {
            var stats = await Mediator.Send(new ClubStatisticsQuery()).ConfigureAwait(false);

            if (stats == null)
            {
                await RespondAsync($"The club is not synced yet.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var builder = new StringBuilder("# Activity statistics of club ");
            builder.AppendLine(stats.ClubName);
            builder.Append("Average average points: ");
            builder.AppendLine(stats.AverageAveragePoints.ToString(CultureInfo.InvariantCulture));
            builder.Append("Min average points: ");
            builder.AppendLine(stats.MinAveragePoints.ToString(CultureInfo.InvariantCulture));
            builder.Append("1. Quartile average points: ");
            builder.AppendLine(stats.FirstQuartileAveragePoints.ToString(CultureInfo.InvariantCulture));
            builder.Append("Median average points: ");
            builder.AppendLine(stats.MedianAveragePoints.ToString(CultureInfo.InvariantCulture));
            builder.Append("3. Quartile average points: ");
            builder.AppendLine(stats.ThirdQuartileAveragePoints.ToString(CultureInfo.InvariantCulture));
            builder.Append("Max average points: ");
            builder.AppendLine(stats.MaxAveragePoints.ToString(CultureInfo.InvariantCulture));

            await RespondAsync(builder.ToString(), ephemeral: true).ConfigureAwait(false);
        }

        [SlashCommand("player-history", "A chart of a member's XP over the weekly checks")]
        public Task ReadPlayerHistoryAsync(
            [Autocomplete(typeof(MemberAutocompleteHandler))][Summary(description: "The member, as suggested")] string member,
            [MinValue(1)][Summary(description: "How many checks to show at most")] int maxNumEntries,
            [Autocomplete(typeof(ClubAutocompleteHandler))][Summary(description: "The club; the member's current club if left out")] string? club = null) =>
            ExecuteAsync(
                async ct =>
                {
                    if (!ClubOption.TryParse(club, out var clubId))
                    {
                        await FollowupAsync(ClubOption.UnknownClubMessage, ephemeral: true).ConfigureAwait(false);
                        return;
                    }

                    var result = await Mediator
                        .Send(new RenderPlayerActivityQuery(member, maxNumEntries, clubId), ct)
                        .ConfigureAwait(false);

                    if (result.IsFailure)
                    {
                        await FollowupAsync(result.Error.Message, ephemeral: true).ConfigureAwait(false);
                        return;
                    }

                    using var history = result.Value;
                    await FollowupWithFileAsync(history.Plot, $"{history.Nickname} history.png", $"Here is the history plot for the player '{history.Nickname}':",
                        ephemeral: true).ConfigureAwait(false);
                },
                ephemeral: true,
                failureMessage: "Failed to generate history plot. Please try again later. If the problem persists, please contact an admin.");

        [SlashCommand("average-leaderboard", "Members ranked by their average rule XP over the last checks")]
        public Task ReadAverageLeaderboardAsync(
            [Autocomplete(typeof(ClubAutocompleteHandler))][Summary(description: "The club; the main club if left out")] string? club = null,
            [MinValue(1)][Summary(description: "How many checks the average covers")] int periods = 4) =>
            ExecuteAsync(
                async ct =>
                {
                    if (!ClubOption.TryParse(club, out var clubId))
                    {
                        await FollowupAsync(ClubOption.UnknownClubMessage, ephemeral: true).ConfigureAwait(false);
                        return;
                    }

                    var (leaderboard, resolvedClubName) = await Mediator
                        .Send(new GetActivityLeaderboardQuery(clubId, periods), ct)
                        .ConfigureAwait(false);

                    if (resolvedClubName is null)
                    {
                        await FollowupAsync(ClubOption.UnknownClubMessage, ephemeral: true).ConfigureAwait(false);
                        return;
                    }

                    var message = BuildLeaderboardMessage(leaderboard!, resolvedClubName, periods);

                    await FollowupAsync(message, ephemeral: true)
                        .ConfigureAwait(false);
                },
                ephemeral: true,
                failureMessage: "Failed to fetch average leaderboard. Please try again later.");

        private static string BuildLeaderboardMessage(List<ClubMemberAverageXp> leaderboard, string clubName, int historyDepth)
        {
            if (leaderboard.Count > 0)
            {
                var builder = new StringBuilder();

                builder.Append($"## Top members of {clubName} by average XP (last {historyDepth} intervals):");
                for (var i = 0; i < leaderboard.Count; i++)
                {
                    builder.AppendLine();
                    builder.Append($"{i + 1}. {leaderboard[i].Nickname} — {leaderboard[i].AverageXp:F1}XP");
                }

                return builder.ToString();
            }
            else
            {
                return "There were no member activities found.";
            }
        }
    }
}

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
        [SlashCommand("player", "Read the statistics of a player")]
        public async Task ReadPlayerStatisticsAsync(
            [Autocomplete(typeof(MemberNicknameAutocompleteHandler))] string memberNickname)
        {
            var stats = await Mediator.Send(new PlayerStatisticsQuery(memberNickname)).ConfigureAwait(false);

            if (stats == null)
            {
                await RespondAsync($"The player {memberNickname} is not being tracked.", ephemeral: true).ConfigureAwait(false);
                return;
            }

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

        [SlashCommand("club", "Read the statistics of the club")]
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

        [SlashCommand("player-history", "Read the history of a player")]
        public Task ReadPlayerHistoryAsync(
            [Autocomplete(typeof(MemberNicknameAutocompleteHandler))] string memberNickname,
            [MinValue(1)][Summary(description: "The maximum number of history entries to visualize")] int maxNumEntries,
            [Autocomplete(typeof(ClubNameAutocompleteHandler))][Summary(description: "[optional] The clubs name")] string? clubName = null) =>
            ExecuteAsync(
                async ct =>
                {
                    using var plot = await Mediator
                        .Send(new RenderPlayerActivityQuery(memberNickname, maxNumEntries, clubName), ct)
                        .ConfigureAwait(false);

                    if (plot == null)
                    {
                        await FollowupAsync($"The player '{memberNickname}' is currently not being tracked.", ephemeral: true).ConfigureAwait(false);
                        return;
                    }

                    await FollowupWithFileAsync(plot, $"{memberNickname} history.png", $"Here is the history plot for the player '{memberNickname}':",
                        ephemeral: true).ConfigureAwait(false);
                },
                ephemeral: true,
                failureMessage: "Failed to generate history plot. Please try again later. If the problem persists, please contact an admin.");

        [SlashCommand("average-leaderboard", "Read the leaderboard of members by average club xp")]
        public Task ReadAverageLeaderboardAsync(
            [Autocomplete(typeof(ClubNameAutocompleteHandler))][Summary(description: "[optional] The clubs name")] string? clubName = null,
            [MinValue(1)][Summary(description: "[optional] The number of intervals to include in the average")] int periods = 4) =>
            ExecuteAsync(
                async ct =>
                {
                    var inputClubName = clubName;

                    var (leaderboard, resolvedClubName) = await Mediator
                        .Send(new GetActivityLeaderboardQuery(clubName, periods), ct)
                        .ConfigureAwait(false);

                    if (resolvedClubName is null)
                    {
                        await FollowupAsync($"The club '{inputClubName ?? "<default>"}' does not exist in the database.", ephemeral: false)
                            .ConfigureAwait(false);
                        return;
                    }

                    var message = BuildLeaderboardMessage(leaderboard!, inputClubName!, periods);

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

using System.Globalization;
using System.Text;
using Discord.Interactions;
using Entities;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.ClubMemberActivity;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

public partial class ActivityModule
{
    public partial class ActivityStatisticsModule(
        IPlayerStatisticsUseCase playerStatisticsUseCase, 
        IRenderPlayerActivityUseCase renderPlayerActivityUseCase,
        IClubStatisticsUseCase clubStatisticsUseCase,
        IGetActivityLeaderboardUseCase getActivityLeaderboardUseCase,
        ILogger<ActivityStatisticsModule> logger)
    {
        [SlashCommand("player", "Read the statistics of a player")]
        public async Task ReadPlayerStatisticsAsync(string memberNickname)
        {
            // Read the stats
            var stats = await playerStatisticsUseCase.GetPlayerStatisticsAsync(memberNickname).ConfigureAwait(false);
            
            // If the player was not found
            if (stats == null)
            {
                await RespondAsync($"The player {memberNickname} is not being tracked.", ephemeral: true).ConfigureAwait(false);
                return;
            }
            
            // Build the message
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
            
            // Respond with the message
            await RespondAsync(builder.ToString(), ephemeral: true).ConfigureAwait(false);
        }
        
        [SlashCommand("club", "Read the statistics of the club")]
        public async Task ReadClubStatisticsAsync()
        {
            // Read the stats
            var stats = await clubStatisticsUseCase.GetClubStatisticsAsync().ConfigureAwait(false);
            
            // If the player was not found
            if (stats == null)
            {
                await RespondAsync($"The club is not synced yet.", ephemeral: true).ConfigureAwait(false);
                return;
            }
            
            // Build the message
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
            
            // Respond with the message
            await RespondAsync(builder.ToString(), ephemeral: true).ConfigureAwait(false);
        }
        
        [SlashCommand("player-history", "Read the history of a player")]
        public async Task ReadPlayerHistoryAsync(
            string memberNickname, 
            [MinValue(1)] [Summary(description: "The maximum number of history entries to visualize")] int maxNumEntries)
        {
            try
            {
                // Defer the response
                await DeferAsync(ephemeral: true).ConfigureAwait(false);
                
                // Try to read the plot
                using var plot = await renderPlayerActivityUseCase
                    .RenderPlayerActivityAsync(memberNickname, maxNumEntries)
                    .ConfigureAwait(false);
                
                // If there is no entry
                if (plot == null)
                {
                    // Send error
                    await FollowupAsync($"The player '{memberNickname}' is currently not being tracked.", ephemeral: true).ConfigureAwait(false);
                    return;
                }
                
                // Respond with the image
                await FollowupWithFileAsync(plot, $"{memberNickname} history.png", $"Here is the history plot for the player '{memberNickname}':",
                    ephemeral: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Log error
                LogErrorWhileCreatingHistoryPlotForPlayer(logger, ex, memberNickname);
                
                // Give error
                await FollowupAsync($"Failed to generate history plot. Please try again later. If the problem persists, please contact an admin.", ephemeral: true).ConfigureAwait(false);
            }
        }

        [SlashCommand("average-leaderboard", "Read the leaderboard of members by average club xp")]
        public async Task ReadAverageLeaderboardAsync(
            [Summary(description: "[optional] The clubs name")] string? clubName = null,
            [MinValue(1)] [Summary(description: "[optional] The number of intervals to include in the average")] int periods = 4)
        {
            try
            {
                // Save input club name
                var inputClubName = clubName;
                
                // Defer the response
                await DeferAsync(ephemeral: true).ConfigureAwait(false);
                
                // Get averages
                (var leaderboard, clubName) = await getActivityLeaderboardUseCase
                    .GetActivityLeaderboardAsync(clubName, periods)
                    .ConfigureAwait(false);
                
                // If the club was not found
                if (clubName is null)
                {
                    // Send error
                    await FollowupAsync($"The club '{inputClubName ?? "<default>"}' does not exist in the database.", ephemeral: false)
                        .ConfigureAwait(false);
                    return;
                }
                
                // Build the message
                var message = _buildLeaderboardMessage(leaderboard!, inputClubName!, periods);
                
                // Send message
                await FollowupAsync(
                        message,
                        ephemeral: true)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogGetAverageLeaderboardFailed(ex, clubName ?? "<default>", periods);
                await FollowupAsync("Failed to fetch average leaderboard. Please try again later.", ephemeral: true)
                    .ConfigureAwait(false);
            }
        }

        private string _buildLeaderboardMessage(List<ClubMemberAverageXp> leaderboard, string clubName, int historyDepth)
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
        
        [LoggerMessage(LogLevel.Error, "Error while creating history plot for player {memberNickname}")]
        static partial void LogErrorWhileCreatingHistoryPlotForPlayer(ILogger<ActivityStatisticsModule> logger, Exception ex, string memberNickname);
        
        [LoggerMessage(LogLevel.Error, "Failed to fetch average leaderboard of club {ClubName} for depth {depth}.")]
        partial void LogGetAverageLeaderboardFailed(Exception ex, string clubName, int depth);
    }
}
using System.Globalization;
using System.Text;
using Discord.Interactions;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters.Commands;

public partial class ActivityModule
{
    public partial class ActivityStatisticsModule(IPlayerStatisticsUseCase playerStatisticsUseCase, IClubStatisticsUseCase clubStatisticsUseCase)
    {
        [SlashCommand("player", "Read the statistics of a player")]
        public async Task ReadPlayerStatisticsAsync(string memberNickname)
        {
            // Read the stats
            var stats = await playerStatisticsUseCase.GetPlayerStatisticsAsync(memberNickname);
            
            // If the player was not found
            if (stats == null)
            {
                await RespondAsync($"The player {memberNickname} is not being tracked.", ephemeral: true);
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
            await RespondAsync(builder.ToString(), ephemeral: true);
        }
        
        [SlashCommand("club", "Read the statistics of the club")]
        public async Task ReadClubStatisticsAsync()
        {
            // Read the stats
            var stats = await clubStatisticsUseCase.GetClubStatisticsAsync();
            
            // If the player was not found
            if (stats == null)
            {
                await RespondAsync($"The club is not synced yet.", ephemeral: true);
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
            await RespondAsync(builder.ToString(), ephemeral: true);
        }
    }
}
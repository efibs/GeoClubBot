using Entities;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMemberActivity;

public class PlayerStatisticsUseCase(IHistoryRepository historyRepository) : IPlayerStatisticsUseCase
{
    public async Task<PlayerStatistics?> GetPlayerStatisticsAsync(string nickname)
    {
        // Get the history entries
        var historyEntries = await historyRepository.ReadHistoryEntriesByPlayerNicknameAsync(nickname);

        // If there are no history entries for the player
        if (historyEntries == null)
        {
            return null;
        }

        // If there are no history entries for the player
        if (historyEntries.Count == 0)
        {
            return new PlayerStatistics(nickname, DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, 0);
        }

        // Get the earliest date time
        var earliestTime = historyEntries.Select(e => e.Timestamp).Min();

        // Get a list of the points
        var points = historyEntries.Select(e => e.Xp).ToList();

        // Calculate the differences
        var pointDifferences = points
            .Skip(1)
            .Zip(points, (a, b) => a - b)
            .Order()
            .ToList();

        // Calculate stats
        var numEntries = pointDifferences.Count;
        var averagePoints = pointDifferences.Average();
        var minPoints = pointDifferences.Min();
        var firstQuartilePoints = pointDifferences.Skip(pointDifferences.Count / 4).First();
        var medianPoints = pointDifferences.Skip(pointDifferences.Count / 2).First();
        var thirdQuartilePoints = pointDifferences.Skip(pointDifferences.Count * 3 / 4).First();
        var maxPoints = pointDifferences.Max();

        return new PlayerStatistics(nickname, earliestTime, numEntries, averagePoints, minPoints, firstQuartilePoints,
            medianPoints, thirdQuartilePoints, maxPoints);
    }
}
using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class ClubStatisticsUseCase(IClubRepository clubRepository, IHistoryRepository historyRepository, IConfiguration config) : IClubStatisticsUseCase
{
    public async Task<ClubStatistics?> GetClubStatisticsAsync()
    {
        // Read the club
        var club = await clubRepository.ReadClubByIdAsync(_clubId);
        
        // If the club was not found
        if (club == null)
        {
            return null;
        }
        
        // Read the entire history
        var history = await historyRepository.ReadHistoryEntriesAsync();
        
        // Group the history by user id and get the average points
        var averagePointsEarned = history
            .GroupBy(e => e.UserId)
            .Select(g => g.Select(e => e.Xp).ToList())
            .Select(g => g
                .Skip(1)
                .Zip(g, (a, b) => a - b)
                .Average())
            .Order()
            .ToList();
        
        // Calculate stats
        var averagePoints = averagePointsEarned.Average();
        var minPoints = averagePointsEarned.Min();
        var firstQuartilePoints = averagePointsEarned.Skip(averagePointsEarned.Count / 4).First();
        var medianPoints = averagePointsEarned.Skip(averagePointsEarned.Count / 2).First();
        var thirdQuartilePoints = averagePointsEarned.Skip(averagePointsEarned.Count * 3 / 4).First();
        var maxPoints = averagePointsEarned.Max();

        return new ClubStatistics(club.Name, averagePoints, minPoints, firstQuartilePoints, medianPoints,
            thirdQuartilePoints, maxPoints);
    }
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}
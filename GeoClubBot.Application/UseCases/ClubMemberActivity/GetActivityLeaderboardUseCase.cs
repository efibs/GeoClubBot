using Entities;
using UseCases.InputPorts.Club;
using UseCases.InputPorts.ClubMemberActivity;

namespace UseCases.UseCases.ClubMemberActivity;

public class GetActivityLeaderboardUseCase(ICalculateAverageXpUseCase calculateAverageXpUseCase, IGetClubByNameOrDefaultUseCase getClubByNameOrDefaultUseCase) : IGetActivityLeaderboardUseCase
{
    public async Task<(List<ClubMemberAverageXp>? Leaderboard, string? ClubName)> GetActivityLeaderboardAsync(string? clubName, int historyDepth)
    {
        // Get the club
        var club = await getClubByNameOrDefaultUseCase
            .GetClubByNameOrDefaultAsync(clubName)
            .ConfigureAwait(false);
        
        // If the club was not found
        if (club is null)
        {
            return (null, null);
        }
        
        // Get the leaderboard
        var leaderboard = await calculateAverageXpUseCase
            .CalculateAverageXpAsync(club.ClubId, historyDepth)
            .ConfigureAwait(false);
        
        return (leaderboard, club.Name);
    }
}
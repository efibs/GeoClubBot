using Entities;

namespace UseCases.InputPorts.ClubMemberActivity;

public interface IGetActivityLeaderboardUseCase
{
    Task<(List<ClubMemberAverageXp>? Leaderboard, string? ClubName)> GetActivityLeaderboardAsync(
        string? clubName,
        int historyDepth
    );
}
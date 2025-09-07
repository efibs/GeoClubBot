using Entities;

namespace UseCases.InputPorts.DailyChallenge;

public interface IDistributeDailyChallengeRolesUseCase
{
    Task DistributeDailyChallengeRolesAsync(List<ClubChallengeResult> results);
}
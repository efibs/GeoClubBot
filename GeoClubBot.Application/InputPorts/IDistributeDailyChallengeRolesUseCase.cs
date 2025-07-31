using Entities;

namespace UseCases.InputPorts;

public interface IDistributeDailyChallengeRolesUseCase
{
    Task DistributeDailyChallengeRolesAsync(List<ClubChallengeResult> results);
}
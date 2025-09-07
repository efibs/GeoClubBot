using Entities;

namespace UseCases.InputPorts.ClubMemberActivity;

public interface IPlayerStatisticsUseCase
{
    Task<PlayerStatistics?> GetPlayerStatisticsAsync(string nickname);
}
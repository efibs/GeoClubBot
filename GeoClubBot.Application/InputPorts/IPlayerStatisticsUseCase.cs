using Entities;

namespace UseCases.InputPorts;

public interface IPlayerStatisticsUseCase
{
    Task<PlayerStatistics?> GetPlayerStatisticsAsync(string nickname);
}
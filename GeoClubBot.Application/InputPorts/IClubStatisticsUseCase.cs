using Entities;

namespace UseCases.InputPorts;

public interface IClubStatisticsUseCase
{
    Task<ClubStatistics?> GetClubStatisticsAsync();
}
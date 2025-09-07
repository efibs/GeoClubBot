using Entities;

namespace UseCases.InputPorts.ClubMemberActivity;

public interface IClubStatisticsUseCase
{
    Task<ClubStatistics?> GetClubStatisticsAsync();
}
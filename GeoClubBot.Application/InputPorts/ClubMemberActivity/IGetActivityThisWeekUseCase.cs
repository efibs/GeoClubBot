using Entities;

namespace UseCases.InputPorts.ClubMemberActivity;

public interface IGetActivityThisWeekUseCase
{
    Task<ClubMemberWeekActivity> GetCurrentWeekActivityForMemberAsync(string userId);
}
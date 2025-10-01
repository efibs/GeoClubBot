using Entities;

namespace UseCases.InputPorts.ClubMemberActivity;

public interface IClubMemberActivityRewardUseCase
{
    Task RewardMemberActivityAsync(List<ClubMemberActivityStatus> statuses);
}
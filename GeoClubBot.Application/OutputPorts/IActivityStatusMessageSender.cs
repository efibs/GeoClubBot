using Entities;

namespace UseCases.OutputPorts;

public interface IActivityStatusMessageSender
{
    Task SendActivityStatusUpdateMessageAsync(List<ClubMemberActivityStatus> statuses, string clubName, int minXP);

    Task SendAverageXpMessageAsync(
        List<ClubMemberAverageXp> topMembers,
        List<ClubMemberAverageXp> bottomMembers,
        string clubName,
        int historyDepth);
}
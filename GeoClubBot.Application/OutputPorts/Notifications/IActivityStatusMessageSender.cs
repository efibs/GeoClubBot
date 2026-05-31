using Entities;

namespace UseCases.OutputPorts.Notifications;

public interface IActivityStatusMessageSender
{
    Task SendActivityStatusUpdateMessageAsync(List<ClubMemberActivityStatus> statuses, string clubName, int minXP, CancellationToken cancellationToken = default);

    Task SendAverageXpMessageAsync(
        List<ClubMemberAverageXp> topMembers,
        List<ClubMemberAverageXp> bottomMembers,
        string clubName,
        int historyDepth,
        CancellationToken cancellationToken = default);
}

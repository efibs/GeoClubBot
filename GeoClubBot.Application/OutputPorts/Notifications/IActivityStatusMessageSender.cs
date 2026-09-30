using Entities;

namespace UseCases.OutputPorts.Notifications;

public interface IActivityStatusMessageSender
{
    /// <param name="requirements">The club's requirements for the header, e.g. "streak 6 · missions 2".</param>
    Task SendActivityStatusUpdateMessageAsync(List<ClubMemberActivityStatus> statuses, string clubName, string requirements, CancellationToken cancellationToken = default);

    Task SendAverageXpMessageAsync(
        List<ClubMemberAverageXp> topMembers,
        List<ClubMemberAverageXp> bottomMembers,
        string clubName,
        int historyDepth,
        CancellationToken cancellationToken = default);
}

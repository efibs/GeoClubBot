using Entities;

namespace UseCases.OutputPorts;

public interface IActivityStatusMessageSender
{
    Task SendActivityStatusUpdateMessageAsync(List<ClubMemberActivityStatus> statuses);
}
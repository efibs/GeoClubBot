using Entities;

namespace UseCases.OutputPorts;

public interface IStatusMessageSender
{
    Task SendActivityStatusUpdateMessageAsync(List<GeoGuessrClubMemberActivityStatus> statuses);
}
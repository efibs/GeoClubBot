using Entities;

namespace UseCases.OutputPorts.Notifications;

public interface IClubEventNotifier
{
    Task SendClubLevelUpEvent(Club club, CancellationToken cancellationToken = default);
}

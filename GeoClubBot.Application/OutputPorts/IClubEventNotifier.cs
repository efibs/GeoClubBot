using Entities;

namespace UseCases.OutputPorts;

public interface IClubEventNotifier
{
    Task SendClubLevelUpEvent(Club club);
}
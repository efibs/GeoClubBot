using Entities;
using UseCases.OutputPorts.GeoGuessr.DTOs;

namespace UseCases.OutputPorts;

public interface IClubEventNotifier
{
    Task SendClubLevelUpEvent(Club club);
}
using Entities;

namespace UseCases.OutputPorts;

public interface IDailyMissionRepository
{
    void AddRange(IEnumerable<DailyMission> missions);
}

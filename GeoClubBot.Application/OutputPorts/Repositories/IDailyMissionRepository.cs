using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IDailyMissionRepository
{
    void AddRange(IEnumerable<DailyMission> missions);
}

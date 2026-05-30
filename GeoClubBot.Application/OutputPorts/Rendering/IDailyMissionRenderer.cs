using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.OutputPorts.Rendering;

public interface IDailyMissionRenderer
{
    string RenderMission(DailyMissionDto mission);
}

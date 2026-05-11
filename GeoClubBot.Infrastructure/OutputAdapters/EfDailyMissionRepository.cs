using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfDailyMissionRepository(GeoClubBotDbContext dbContext) : IDailyMissionRepository
{
    public void AddRange(IEnumerable<DailyMission> missions)
    {
        dbContext.DailyMissions.AddRange(missions);
    }
}

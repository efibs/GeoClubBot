using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfClubMissionBoardObservationRepository(GeoClubBotDbContext dbContext) : IClubMissionBoardObservationRepository
{
    public Task<ClubMissionBoardObservation?> ReadForUpdateAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        dbContext.ClubMissionBoardObservations.FirstOrDefaultAsync(o => o.ClubId == clubId, cancellationToken);

    public void Add(ClubMissionBoardObservation observation) => dbContext.ClubMissionBoardObservations.Add(observation);
}

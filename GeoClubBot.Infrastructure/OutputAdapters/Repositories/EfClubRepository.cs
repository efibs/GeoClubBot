using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfClubRepository(GeoClubBotDbContext dbContext) : IClubRepository
{
    public Club CreateClub(Club club)
    {
        dbContext.Add(club);
        return club;
    }

    public async Task<Club> CreateOrUpdateClubAsync(Club club, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Clubs
            .FirstOrDefaultAsync(c => c.ClubId == club.ClubId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            dbContext.Add(club);
            return club;
        }

        // Refresh only the fields carried by the GeoGuessr sync payload. Mutating the tracked entity
        // in place preserves server-owned columns such as LatestActivityCheckTime — a blind
        // Update(club) would reset them to the incoming (null) value on every sync.
        existing.Rename(club.Name);
        existing.UpdateLevel(club.Level);
        return existing;
    }

    public async Task<Club?> ReadClubByIdAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Clubs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClubId == clubId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Club?> ReadForUpdateByIdAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Clubs
            .FirstOrDefaultAsync(c => c.ClubId == clubId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Club?> ReadClubByNameAsync(string clubName, CancellationToken cancellationToken = default)
    {
        return await dbContext.Clubs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == clubName, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Club>> ReadAllClubsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Clubs
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

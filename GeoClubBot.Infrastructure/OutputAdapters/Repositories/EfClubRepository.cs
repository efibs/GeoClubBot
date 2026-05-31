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
        var clubExists = await dbContext.Clubs
            .AnyAsync(c => c.ClubId == club.ClubId, cancellationToken)
            .ConfigureAwait(false);

        if (clubExists)
        {
            dbContext.Update(club);
        }
        else
        {
            dbContext.Add(club);
        }

        return club;
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
}

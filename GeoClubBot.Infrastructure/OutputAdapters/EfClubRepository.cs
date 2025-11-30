using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfClubRepository(GeoClubBotDbContext dbContext) : IClubRepository
{
    public Club CreateClub(Club club)
    {
        // Add the club
        dbContext.Add(club);

        return club;
    }

    public async Task<Club> CreateOrUpdateClubAsync(Club club)
    {
        // Try to find an existing club with that id
        var clubExists = await dbContext.Clubs.AnyAsync(c => c.ClubId == club.ClubId).ConfigureAwait(false);

        // If the club already exists
        if (clubExists)
        {
            // Update the club. This is ok in this case because
            // the club has no child entities and everything should be 
            // updated.
            dbContext.Update(club);
        }
        else
        {
            // Add the club
            dbContext.Add(club);
        }

        return club;
    }

    public async Task<Club?> ReadClubByIdAsync(Guid clubId)
    {
        // Try to find the club
        var club = await dbContext.Clubs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClubId == clubId)
            .ConfigureAwait(false);

        return club;
    }
}
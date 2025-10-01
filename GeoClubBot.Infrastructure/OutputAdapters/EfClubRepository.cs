using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfClubRepository(GeoClubBotDbContext dbContext) : IClubRepository
{
    public async Task<Club?> CreateClubAsync(Club club)
    {
        // Try to find an existing club with that id
        var clubExists = await dbContext.Clubs.AnyAsync(c => c.ClubId == club.ClubId).ConfigureAwait(false);

        // If the club already exists
        if (clubExists)
        {
            return null;
        }

        // Add the club
        dbContext.Add(club);

        // Save the changes to the database
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        return club;
    }

    public async Task<Club> CreateOrUpdateClubAsync(Club club)
    {
        // Try to find an existing club with that id
        var clubExists = await dbContext.Clubs.AnyAsync(c => c.ClubId == club.ClubId).ConfigureAwait(false);

        // If the club already exists
        if (clubExists)
        {
            // Update the club
            dbContext.Update(club);
        }
        else
        {
            // Add the club
            dbContext.Add(club);
        }

        // Save the changes to the database
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        return club;
    }

    public async Task<Club?> ReadClubByIdAsync(Guid clubId)
    {
        // Try to find the club
        var club = await dbContext.Clubs.FindAsync(clubId).ConfigureAwait(false);

        return club;
    }
}
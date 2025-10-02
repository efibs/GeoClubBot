using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfGeoGuessrUserRepository(GeoClubBotDbContext dbContext) : IGeoGuessrUserRepository
{
    public async Task<GeoGuessrUser> CreateOrUpdateUserAsync(GeoGuessrUser user)
    {
        // Try to find an existing user with that id
        var userExists = await dbContext.GeoGuessrUsers
            .AnyAsync(u => u.UserId == user.UserId)
            .ConfigureAwait(false);

        // If the club member already exists
        if (userExists)
        {
            // Update the club member
            dbContext.Update(user);
        }
        else
        {
            // Add the club member
            dbContext.Add(user);
        }

        // Save the changes to the database
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        return user;
    }

    public async Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId)
    {
        // Try to find the user
        var clubMember = await dbContext.GeoGuessrUsers
            .FindAsync(userId)
            .ConfigureAwait(false);
        
        // If the club member was not found
        if (clubMember == null)
        {
            return null;
        }
        
        // Detach the entity from the context.
        // This disables change tracking and improves 
        // performance. In this case this is also possible
        // because the GeoGuessrUser entity doesn't have any 
        // relationships.
        dbContext.Entry(clubMember).State = EntityState.Detached;
        
        return clubMember;
    }
    
    public async Task<GeoGuessrUser?> ReadUserByDiscordUserIdAsync(ulong discordUserId)
    {
        // Try to find the user
        var clubMember = await dbContext.GeoGuessrUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.DiscordUserId == discordUserId)
            .ConfigureAwait(false);

        return clubMember;
    }

    public async Task<List<GeoGuessrUser>> ReadAllLinkedUsersAsync()
    {
        // Get the club members that have a discord user id set
        var linkedUsers = await dbContext.GeoGuessrUsers
            .Where(u => u.DiscordUserId.HasValue)
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);
        
        return linkedUsers;
    }
}
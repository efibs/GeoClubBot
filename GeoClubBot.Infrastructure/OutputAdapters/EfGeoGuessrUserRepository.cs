using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfGeoGuessrUserRepository(GeoClubBotDbContext dbContext) : IGeoGuessrUserRepository
{
    public GeoGuessrUser CreateUser(GeoGuessrUser user)
    {
        // Add the club member
        dbContext.Add(user);

        return user;
    }
    
    public GeoGuessrUser UpdateUser(GeoGuessrUser user)
    {
        // Update the user. This is ok in this case because
        // the user has no child entities and everything should be 
        // updated.
        dbContext.Update(user);

        return user;
    }

    public async Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId)
    {
        // Try to find the user
        var clubMember = await dbContext.GeoGuessrUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId)
            .ConfigureAwait(false);

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
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
    
    public async Task<GeoGuessrUser?> UpdateUserAsync(GeoGuessrUser user)
    {
        var dbEntry = await dbContext.GeoGuessrUsers
            .FindAsync(user.UserId)
            .ConfigureAwait(false);

        if (dbEntry is null)
        {
            return null;
        }

        dbEntry.Nickname = user.Nickname;

        return dbEntry;
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

    public async Task<GeoGuessrUser?> LinkDiscordAccountAsync(string userId, ulong discordUserId)
    {
        var dbEntry = await dbContext.GeoGuessrUsers
            .FindAsync(userId)
            .ConfigureAwait(false);

        if (dbEntry is null)
        {
            return null;
        }

        dbEntry.DiscordUserId = discordUserId;

        return dbEntry;
    }

    public async Task<GeoGuessrUser?> UnlinkDiscordAccountAsync(string userId)
    {
        var dbEntry = await dbContext.GeoGuessrUsers
            .FindAsync(userId)
            .ConfigureAwait(false);

        if (dbEntry is null)
        {
            return null;
        }

        dbEntry.DiscordUserId = null;

        return dbEntry;
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
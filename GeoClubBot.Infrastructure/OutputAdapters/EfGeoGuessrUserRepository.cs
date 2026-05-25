using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfGeoGuessrUserRepository(GeoClubBotDbContext dbContext) : IGeoGuessrUserRepository
{
    public void AddUser(GeoGuessrUser user)
    {
        dbContext.Add(user);
    }

    public async Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId)
    {
        return await dbContext.GeoGuessrUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId)
            .ConfigureAwait(false);
    }

    public async Task<GeoGuessrUser?> ReadForUpdateByUserIdAsync(string userId)
    {
        return await dbContext.GeoGuessrUsers
            .FirstOrDefaultAsync(u => u.UserId == userId)
            .ConfigureAwait(false);
    }

    public async Task<GeoGuessrUser?> ReadUserByDiscordUserIdAsync(ulong discordUserId)
    {
        return await dbContext.GeoGuessrUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.DiscordUserId == discordUserId)
            .ConfigureAwait(false);
    }

    public async Task<GeoGuessrUser?> ReadForUpdateByDiscordUserIdAsync(ulong discordUserId)
    {
        return await dbContext.GeoGuessrUsers
            .SingleOrDefaultAsync(u => u.DiscordUserId == discordUserId)
            .ConfigureAwait(false);
    }

    public async Task<List<GeoGuessrUser>> ReadAllLinkedUsersAsync()
    {
        return await dbContext.GeoGuessrUsers
            .AsNoTracking()
            .Where(u => u.DiscordUserId.HasValue)
            .ToListAsync()
            .ConfigureAwait(false);
    }
}

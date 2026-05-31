using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfGeoGuessrUserRepository(GeoClubBotDbContext dbContext) : IGeoGuessrUserRepository
{
    public void AddUser(GeoGuessrUser user)
    {
        dbContext.Add(user);
    }

    public async Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.GeoGuessrUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GeoGuessrUser?> ReadForUpdateByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.GeoGuessrUsers
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GeoGuessrUser?> ReadUserByDiscordUserIdAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        return await dbContext.GeoGuessrUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.DiscordUserId == discordUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GeoGuessrUser?> ReadForUpdateByDiscordUserIdAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        return await dbContext.GeoGuessrUsers
            .SingleOrDefaultAsync(u => u.DiscordUserId == discordUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<GeoGuessrUser>> ReadAllLinkedUsersAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.GeoGuessrUsers
            .AsNoTracking()
            .Where(u => u.DiscordUserId.HasValue)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

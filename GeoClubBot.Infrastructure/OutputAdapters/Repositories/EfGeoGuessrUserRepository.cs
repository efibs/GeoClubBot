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
        // FindAsync checks the change tracker (including entities that were Added but not yet saved)
        // before querying the database. A LINQ query hits the database only, so it would miss a user
        // that was just synced into the same unit of work but not yet committed — the cause of the
        // "user not found after syncing" error on the first account-linking attempt for a new user.
        // UserId is the primary key (ValueGeneratedNever), so a key lookup is exact here.
        return await dbContext.GeoGuessrUsers
            .FindAsync([userId], cancellationToken)
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

    public async Task<List<string>> ReadAllLinkedNicknamesAsync(CancellationToken cancellationToken = default)
    {
        // Projection-only read for autocomplete: never materializes full GeoGuessrUser entities.
        return await dbContext.GeoGuessrUsers
            .AsNoTracking()
            .Where(u => u.DiscordUserId.HasValue)
            .Select(u => u.Nickname)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

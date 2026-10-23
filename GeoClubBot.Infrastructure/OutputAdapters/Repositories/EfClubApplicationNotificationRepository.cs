using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfClubApplicationNotificationRepository(GeoClubBotDbContext dbContext) : IClubApplicationNotificationRepository
{
    public async Task<HashSet<string>> ReadNotifiedUserIdsAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        var userIds = await dbContext.ClubApplicationNotifications
            .AsNoTracking()
            .Where(n => n.ClubId == clubId)
            .Select(n => n.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return userIds.ToHashSet();
    }

    public void Add(ClubApplicationNotification notification)
    {
        dbContext.ClubApplicationNotifications.Add(notification);
    }

    public async Task<int> DeleteAsync(Guid clubId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubApplicationNotifications
            .Where(n => n.ClubId == clubId && userIds.Contains(n.UserId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

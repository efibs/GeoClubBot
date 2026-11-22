using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfMissionBoardSubscriptionRepository(GeoClubBotDbContext dbContext) : IMissionBoardSubscriptionRepository
{
    public Task<MissionBoardSubscription?> ReadForUpdateAsync(ulong discordUserId, CancellationToken cancellationToken = default) =>
        dbContext.MissionBoardSubscriptions.FirstOrDefaultAsync(s => s.DiscordUserId == discordUserId, cancellationToken);

    public Task<List<MissionBoardSubscriber>> ReadSubscribersOfClubAsync(Guid clubId, CancellationToken cancellationToken = default) =>
        (from subscription in dbContext.MissionBoardSubscriptions.AsNoTracking()
         join user in dbContext.GeoGuessrUsers.AsNoTracking() on (ulong?)subscription.DiscordUserId equals user.DiscordUserId
         join member in dbContext.ClubMembers.AsNoTracking() on user.UserId equals member.UserId
         where member.ClubId == clubId
         orderby subscription.SubscribedAt
         select new MissionBoardSubscriber(subscription.DiscordUserId, user.UserId, subscription.Mode))
        .ToListAsync(cancellationToken);

    public void Add(MissionBoardSubscription subscription) => dbContext.MissionBoardSubscriptions.Add(subscription);

    public async Task<bool> DeleteAsync(ulong discordUserId, CancellationToken cancellationToken = default) =>
        await dbContext.MissionBoardSubscriptions
            .Where(s => s.DiscordUserId == discordUserId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false) > 0;
}

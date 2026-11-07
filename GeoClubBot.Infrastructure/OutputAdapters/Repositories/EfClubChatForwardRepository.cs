using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfClubChatForwardRepository(GeoClubBotDbContext dbContext) : IClubChatForwardRepository
{
    public async Task<IReadOnlyList<ClubChatForward>> ReadByMessageAsync(ulong discordMessageId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubChatForwards
            .AsNoTracking()
            .Where(f => f.DiscordMessageId == discordMessageId)
            .OrderBy(f => f.ForwardedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(ClubChatForward forward)
    {
        dbContext.ClubChatForwards.Add(forward);
    }
}

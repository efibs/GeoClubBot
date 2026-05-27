using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfClubChallengeRepository(GeoClubBotDbContext dbContext) : IClubChallengeRepository
{
    public void AddLatestClubChallengeLinks(IEnumerable<ClubChallengeLink> links)
    {
        dbContext.LatestClubChallengeLinks.AddRange(links);
    }

    public async Task<List<ClubChallengeLink>> ReadLatestClubChallengeLinksAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.LatestClubChallengeLinks
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void DeleteLatestClubChallengeLinks(IEnumerable<ClubChallengeLink> links)
    {
        dbContext.LatestClubChallengeLinks.RemoveRange(links);
    }
}

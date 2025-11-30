using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfClubChallengeRepository(GeoClubBotDbContext dbContext) : IClubChallengeRepository
{
    public List<ClubChallengeLink> CreateLatestClubChallengeLinks(ICollection<ClubChallengeLink> links)
    {
        // Add the links
        dbContext.LatestClubChallengeLinks.AddRange(links);
        
        return links.ToList();
    }

    public async Task<List<ClubChallengeLink>> ReadLatestClubChallengeLinksAsync()
    {
        // Read the links
        var links = await dbContext.LatestClubChallengeLinks
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);
        
        return links;
    }

    public void DeleteLatestClubChallengeLinks(ICollection<ClubChallengeLink> links)
    {
        // Delete the links
        dbContext.LatestClubChallengeLinks.RemoveRange(links);
    }
}
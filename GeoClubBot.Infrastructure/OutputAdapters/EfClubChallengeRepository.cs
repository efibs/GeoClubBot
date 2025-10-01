using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfClubChallengeRepository(GeoClubBotDbContext dbContext) : IClubChallengeRepository
{
    public async Task<List<ClubChallengeLink>> CreateLatestClubChallengeLinksAsync(List<ClubChallengeLink> links)
    {
        // Add every link
        foreach (var link in links)
        {
            dbContext.Add(link);
        }
        
        // Save the changes to the database
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
        
        return links;
    }

    public async Task<List<ClubChallengeLink>> ReadLatestClubChallengeLinksAsync()
    {
        // Read the links
        var links = await dbContext.LatestClubChallengeLinks.ToListAsync().ConfigureAwait(false);
        
        return links;
    }

    public async Task<int> DeleteLatestClubChallengeLinksAsync(List<int> linkIds)
    {
        // Delete the links
        var numDeleted = await dbContext.LatestClubChallengeLinks
            .Where(x => linkIds.Contains(x.Id))
            .ExecuteDeleteAsync().ConfigureAwait(false);
        
        return numDeleted;
    }
}
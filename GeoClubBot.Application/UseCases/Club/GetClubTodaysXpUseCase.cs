using UseCases.InputPorts.Club;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Club;

public class GetClubTodaysXpUseCase(
    IGeoGuessrClient geoGuessrClient,
    IGetClubByNameOrDefaultUseCase getClubByNameOrDefaultUseCase) : IGetClubTodaysXpUseCase
{
    public async Task<(int? Xp, string? ClubName)> GetTodaysXpAsync(string? clubName, bool includeWeeklies)
    {
        // Read the club
        var club = await getClubByNameOrDefaultUseCase.GetClubByNameOrDefaultAsync(clubName).ConfigureAwait(false);

        // If the club could not be found 
        if (club is null)
        {
            return (null, null);
        }
        
        // Get today
        var today = DateTimeOffset.UtcNow.Date;
        
        // Count the XP
        var xp = 0;
        
        // The current pagination token
        string? paginationToken = null;
        
        // While true - iterate until break
        while (true)
        {
            // Build the request
            var request = new ReadClubActivitiesQueryParams
            {
                PaginationToken = paginationToken
            };
            
            // Get the batch
            var activitiesBatch = await geoGuessrClient
                .ReadClubActivitiesAsync(club.ClubId, request)
                .ConfigureAwait(false);
            
            // Break condition
            if (activitiesBatch.Items.Count == 0)
            {
                return (xp, club.Name);
            }
            
            // Order by timestamp
            var orderedActivities = activitiesBatch.Items
                .OrderByDescending(i => i.RecordedAt);

            // For every activity
            foreach (var activity in orderedActivities)
            {
                // If the item is no longer the current day
                if (activity.RecordedAt.Date < today)
                {
                    return (xp, club.Name);
                }
                
                // If weeklies should not be included and this is a weekly
                if (includeWeeklies == false && activity.XpReward == 1000)
                {
                    continue;
                }
                
                // Add xp
                xp += activity.XpReward;
            }
            
            // Break condition
            if (activitiesBatch.PaginationToken is null)
            {
                return (xp, club.Name);
            }
            
            // Update the pagination token
            paginationToken = activitiesBatch.PaginationToken;
        }
    }
}
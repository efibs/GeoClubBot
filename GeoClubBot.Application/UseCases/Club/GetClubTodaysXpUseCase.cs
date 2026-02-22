using Configuration;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.Club;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Club;

public class GetClubTodaysXpUseCase(
    IGeoGuessrClient geoGuessrClient,
    IUnitOfWork unitOfWork,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IGetClubTodaysXpUseCase
{
    public async Task<(int? Xp, string? ClubName)> GetTodaysXpAsync(string? clubName, bool includeWeeklies)
    {
        // Get the club id
        var clubId = await _getClubIdAsync(clubName).ConfigureAwait(false);
        
        // If the club could not be found 
        if (clubId is null)
        {
            return (null, null);
        }
        
        // If the club name is not given yet
        if (string.IsNullOrWhiteSpace(clubName))
        {
            // Read the clubs name
            var club = await unitOfWork.Clubs.ReadClubByIdAsync(clubId.Value).ConfigureAwait(false);
            
            // If the club was not found
            if (club is null)
            {
                return (null, null);
            }
            
            // Set the club name
            clubName = club.Name;
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
                .ReadClubActivitiesAsync(clubId.Value, request)
                .ConfigureAwait(false);
            
            // Break condition
            if (activitiesBatch.Items.Count == 0)
            {
                return (xp, clubName);
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
                    return (xp, clubName);
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
                return (xp, clubName);
            }
            
            // Update the pagination token
            paginationToken = activitiesBatch.PaginationToken;
        }
    }

    private async Task<Guid?> _getClubIdAsync(string? clubName)
    {
        // If the club is not set
        if (string.IsNullOrWhiteSpace(clubName))
        {
            // Use the default club id
            return _defaultClubId;
        }
        
        // Look for the club by name
        var club = await unitOfWork.Clubs.ReadClubByNameAsync(clubName).ConfigureAwait(false);

        return club?.ClubId;
    }

    private readonly Guid _defaultClubId = geoGuessrConfig.Value.MainClub.ClubId;
}
using UseCases.InputPorts.Club;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Club;

public class GetClubTodaysXpUseCase(
    IGeoGuessrActivityReader activityReader,
    IGetClubByNameOrDefaultUseCase getClubByNameOrDefaultUseCase) : IGetClubTodaysXpUseCase
{
    public async Task<(int? Xp, string? ClubName)> GetTodaysXpAsync(string? clubName, bool includeWeeklies)
    {
        var club = await getClubByNameOrDefaultUseCase.GetClubByNameOrDefaultAsync(clubName).ConfigureAwait(false);

        if (club is null)
        {
            return (null, null);
        }

        var todaysActivities = await activityReader
            .ReadTodaysActivitiesAsync(club.ClubId)
            .ConfigureAwait(false);

        var xp = todaysActivities
            .Where(a => includeWeeklies || a.XpReward != 1000)
            .Sum(a => a.XpReward);

        return (xp, club.Name);
    }
}

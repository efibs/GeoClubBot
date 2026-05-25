using System.Collections.Concurrent;
using Infrastructure.OutputAdapters.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

public class GeoGuessrClientFactory(IHttpClientFactory httpClientFactory) : IGeoGuessrClientFactory
{
    public const string ActivityHttpClientName = "GeoGuessr_Activity";
    public const string MissionsHttpClientName = "GeoGuessr_Missions";
    public const string UserProfileHttpClientName = "GeoGuessr_UserProfile";

    private readonly ConcurrentDictionary<Guid, IGeoGuessrClient> _clients = new();
    private IGeoGuessrClient? _activityClient;
    private IGeoGuessrClient? _missionsClient;
    private IGeoGuessrClient? _userProfileClient;

    public IGeoGuessrClient CreateClient(Guid clubId)
    {
        return _clients.GetOrAdd(clubId, id =>
            RefitGeoGuessrClient.FromHttpClient(httpClientFactory.CreateClient($"GeoGuessr_{id}")));
    }

    public IGeoGuessrClient CreateActivityClient()
    {
        return _activityClient ??= RefitGeoGuessrClient.FromHttpClient(
            httpClientFactory.CreateClient(ActivityHttpClientName));
    }

    public IGeoGuessrClient CreateMissionsClient()
    {
        return _missionsClient ??= RefitGeoGuessrClient.FromHttpClient(
            httpClientFactory.CreateClient(MissionsHttpClientName));
    }

    public IGeoGuessrClient CreateUserProfileClient()
    {
        return _userProfileClient ??= RefitGeoGuessrClient.FromHttpClient(
            httpClientFactory.CreateClient(UserProfileHttpClientName));
    }
}

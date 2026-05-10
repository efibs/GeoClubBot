using System.Collections.Concurrent;
using Refit;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

public class GeoGuessrClientFactory(IHttpClientFactory httpClientFactory) : IGeoGuessrClientFactory
{
    public const string ActivityHttpClientName = "GeoGuessr_Activity";

    private readonly ConcurrentDictionary<Guid, IGeoGuessrClient> _clients = new();
    private IGeoGuessrClient? _activityClient;

    public IGeoGuessrClient CreateClient(Guid clubId)
    {
        return _clients.GetOrAdd(clubId, id =>
        {
            var httpClient = httpClientFactory.CreateClient($"GeoGuessr_{id}");
            return RestService.For<IGeoGuessrClient>(httpClient);
        });
    }

    public IGeoGuessrClient CreateActivityClient()
    {
        return _activityClient ??= RestService.For<IGeoGuessrClient>(
            httpClientFactory.CreateClient(ActivityHttpClientName));
    }
}

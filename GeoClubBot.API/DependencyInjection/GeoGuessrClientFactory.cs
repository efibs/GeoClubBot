using System.Collections.Concurrent;
using Refit;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

public class GeoGuessrClientFactory(IHttpClientFactory httpClientFactory) : IGeoGuessrClientFactory
{
    private readonly ConcurrentDictionary<Guid, IGeoGuessrClient> _clients = new();

    public IGeoGuessrClient CreateClient(Guid clubId)
    {
        return _clients.GetOrAdd(clubId, id =>
        {
            var httpClient = httpClientFactory.CreateClient($"GeoGuessr_{id}");
            return RestService.For<IGeoGuessrClient>(httpClient);
        });
    }
}

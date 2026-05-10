using GeoClubBot.MockGeoGuessr.DataStore;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.MockGeoGuessr.Client;

public class MockGeoGuessrClientFactory(MockGeoGuessrDataStore dataStore) : IGeoGuessrClientFactory
{
    public IGeoGuessrClient CreateClient(Guid clubId)
    {
        return new MockGeoGuessrClient(dataStore);
    }

    public IGeoGuessrClient CreateActivityClient()
    {
        return new MockGeoGuessrClient(dataStore);
    }
}

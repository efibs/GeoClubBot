using GeoClubBot.MockGeoGuessr.DataStore;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.MockGeoGuessr.Client;

public class MockGeoGuessrClientFactory(MockGeoGuessrDataStore dataStore) : IGeoGuessrClientFactory
{
    public IGeoGuessrClient CreateClient(Guid clubId)
    {
        // The club travels with the client like a club's token does on the live API: it is how the
        // board endpoints know which club to answer for.
        return new MockGeoGuessrClient(dataStore, clubId);
    }

    public IGeoGuessrClient CreateActivityClient()
    {
        return new MockGeoGuessrClient(dataStore);
    }

    public IGeoGuessrClient CreateUserProfileClient()
    {
        return new MockGeoGuessrClient(dataStore);
    }
}

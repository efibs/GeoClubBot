using GeoClubBot.MockGeoGuessr.Client;
using GeoClubBot.MockGeoGuessr.DataStore;
using GeoClubBot.MockGeoGuessr.Endpoints;
using GeoClubBot.MockGeoGuessr.Initialization;
using Microsoft.Extensions.DependencyInjection;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.MockGeoGuessr.DependencyInjection;

public static class MockGeoGuessrServices
{
    public static IServiceCollection AddMockGeoGuessrServices(this IServiceCollection services)
    {
        services.AddSingleton<MockGeoGuessrDataStore>();
        services.AddSingleton<IGeoGuessrClient, MockGeoGuessrClient>();
        services.AddSingleton<IGeoGuessrClientFactory, MockGeoGuessrClientFactory>();
        services.AddHostedService<MockGeoGuessrDataInitializer>();

        // Register controllers from the mock assembly
        services.AddControllers()
            .AddApplicationPart(typeof(MockManagementController).Assembly);

        return services;
    }
}

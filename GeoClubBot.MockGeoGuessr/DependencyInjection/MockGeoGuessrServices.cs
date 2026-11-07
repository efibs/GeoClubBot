using GeoClubBot.MockGeoGuessr.Client;
using GeoClubBot.MockGeoGuessr.DataStore;
using GeoClubBot.MockGeoGuessr.Endpoints;
using GeoClubBot.MockGeoGuessr.Initialization;
using GeoClubBot.MockGeoGuessr.Socket;
using Infrastructure.InputAdapters.GeoGuessrSocket;
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

        // GeoGuessr's club socket, in process. Registered before the bot's own socket module, whose
        // real connector then stays out.
        services.AddSingleton<MockClubSocketHub>();
        services.AddSingleton<IGeoGuessrSocketConnector, MockGeoGuessrSocketConnector>();

        // Register controllers from the mock assembly
        services.AddControllers()
            .AddApplicationPart(typeof(MockManagementController).Assembly);

        return services;
    }
}

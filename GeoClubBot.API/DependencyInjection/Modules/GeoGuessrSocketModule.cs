using Infrastructure.InputAdapters.GeoGuessrSocket;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoClubBot.DependencyInjection.Modules;

public static class GeoGuessrSocketModule
{
    /// <summary>
    /// GeoGuessr's club socket (see <see cref="GeoGuessrSocketService"/>). Registered after the Discord
    /// adapters so it starts once the start-up sync is done (hosted services start in registration order).
    /// </summary>
    public static IServiceCollection AddGeoGuessrSocketModule(this IServiceCollection services)
    {
        // The mock GeoGuessr registers its in-process connector first; it is kept.
        services.TryAddSingleton<IGeoGuessrSocketConnector, ClientWebSocketConnector>();

        services.AddSingleton<SocketFrameReports>();
        services.AddSingleton<ClubEventBatcher>();
        services.AddSingleton<IClubEventSink>(provider => provider.GetRequiredService<ClubEventBatcher>());
        services.AddHostedService<GeoGuessrSocketService>();

        return services;
    }
}

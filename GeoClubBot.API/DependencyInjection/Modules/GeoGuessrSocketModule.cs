using Infrastructure.InputAdapters.GeoGuessrSocket;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.ClubChatForwarding;

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

        // Forwarding into a club's chat works with the socket feature off too: the sender then opens
        // a connection of its own.
        services.AddSingleton<ClubChatChannels>();
        services.AddSingleton<IGeoGuessrClubChatSender, GeoGuessrClubChatSender>();
        services.AddScoped<ClubChatForwardingAccess>();
        services.AddSingleton<ClubEventBatcher>();
        services.AddSingleton<IClubEventSink>(provider => provider.GetRequiredService<ClubEventBatcher>());
        services.AddHostedService<GeoGuessrSocketService>();

        return services;
    }
}

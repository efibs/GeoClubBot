using System.Diagnostics.Metrics;

namespace UseCases.Observability;

/// <summary>
/// GeoGuessr's club socket. Recorded by the socket adapter (Infrastructure) and the batch handler, so
/// a dashboard shows whether the sockets hold and whether GeoGuessr's payloads still carry what the
/// bot expects (<see cref="PayloadFallbacks"/> climbing means they don't).
/// </summary>
public static class GeoGuessrSocketMetrics
{
    public static readonly Counter<long> Frames = HandlerMetrics.Meter.CreateCounter<long>(
        name: "geoclubbot.geoguessr_socket.frames",
        unit: "{frame}",
        description: "Frames received on GeoGuessr's club socket, by code and how they were handled.");

    public static readonly Counter<long> Connections = HandlerMetrics.Meter.CreateCounter<long>(
        name: "geoclubbot.geoguessr_socket.connections",
        unit: "{event}",
        description: "Connects, closes and refused handshakes of GeoGuessr's club socket.");

    public static readonly Counter<long> PayloadFallbacks = HandlerMetrics.Meter.CreateCounter<long>(
        name: "geoclubbot.geoguessr_socket.payload_fallbacks",
        unit: "{frame}",
        description: "Member frames whose payload lacked what the bot needs, so the roster was read again instead.");

    public static readonly Counter<long> Batches = HandlerMetrics.Meter.CreateCounter<long>(
        name: "geoclubbot.geoguessr_socket.batches",
        unit: "{batch}",
        description: "Batches of GeoGuessr socket events applied.");

    public static readonly Counter<long> ChatForwards = HandlerMetrics.Meter.CreateCounter<long>(
        name: "geoclubbot.geoguessr_socket.chat_forwards",
        unit: "{forward}",
        description: "Discord messages forwarded into a club's chat, by outcome and whether the live socket carried them.");

    public static readonly UpDownCounter<long> LiveClubs = HandlerMetrics.Meter.CreateUpDownCounter<long>(
        name: "geoclubbot.geoguessr_socket.live_clubs",
        unit: "{club}",
        description: "Clubs whose GeoGuessr socket is connected and subscribed.");
}

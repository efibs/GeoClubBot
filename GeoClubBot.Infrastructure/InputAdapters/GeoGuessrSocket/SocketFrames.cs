using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>The socket topics the bot subscribes to.</summary>
public static class SocketTopics
{
    /// <summary>A club's own topic: member, club and mission board events. GeoGuessr's ids are lower-case GUIDs.</summary>
    public static string Club(Guid clubId) => $"club:{clubId:D}";

    /// <summary>
    /// A club's chat. Subscribing gets its messages (the bot's own included, which is how a forward is
    /// confirmed), and a <c>ChatMessage</c> sent to it writes into it.
    /// </summary>
    public static string ClubChat(Guid clubId) => $"chat:Club:TextMessages:{clubId:D}";
}

/// <summary>
/// The frames the bot sends. Apart from forwarding an admin's message into the club chat
/// (<see cref="ChatMessage"/>), the bot only listens.
/// </summary>
public static class OutboundFrames
{
    private const string Client = "web";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The site sends this every 15 s. The server doesn't answer it; dead connections show through WebSocket pings.</summary>
    public static string HeartBeat { get; } = JsonSerializer.Serialize(new Frame("HeartBeat", null, null, null), SerializerOptions);

    public static string Subscribe(string topic) => JsonSerializer.Serialize(new Frame("Subscribe", topic, null, Client), SerializerOptions);

    /// <summary>Writes <paramref name="text"/> into a chat. Unlike in inbound frames, the payload is the bare text, not JSON.</summary>
    public static string ChatMessage(string topic, string text) =>
        JsonSerializer.Serialize(new Frame("ChatMessage", topic, text, Client), SerializerOptions);

    private sealed record Frame(string Code, string? Topic, string? Payload, string? Client);
}

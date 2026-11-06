using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>The socket topics the bot subscribes to.</summary>
public static class SocketTopics
{
    /// <summary>A club's own topic: member, club and mission board events. GeoGuessr's ids are lower-case GUIDs.</summary>
    public static string Club(Guid clubId) => $"club:{clubId:D}";
}

/// <summary>The frames the bot sends. Nothing else is ever sent: the bot only listens.</summary>
public static class OutboundFrames
{
    private const string Client = "web";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The site sends this every 15 s. The server doesn't answer it; dead connections show through WebSocket pings.</summary>
    public static string HeartBeat { get; } = JsonSerializer.Serialize(new Frame("HeartBeat", null, null), SerializerOptions);

    public static string Subscribe(string topic) => JsonSerializer.Serialize(new Frame("Subscribe", topic, Client), SerializerOptions);

    private sealed record Frame(string Code, string? Topic, string? Client);
}

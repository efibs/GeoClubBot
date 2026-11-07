using System.Collections.Concurrent;
using System.Text.Json;
using GeoClubBot.MockGeoGuessr.DataStore;
using Infrastructure.InputAdapters.GeoGuessrSocket;

namespace GeoClubBot.MockGeoGuessr.Socket;

/// <summary>
/// The mock's side of GeoGuessr's socket, in process: the bot's sessions subscribe here, and the mock
/// UI's changes (members, club, mission board) are published as the frames GeoGuessr's client code
/// expects — the same shapes the bot's decoder assumes, so local runs exercise the whole path.
/// </summary>
public sealed class MockClubSocketHub(MockGeoGuessrDataStore store)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<MockGeoGuessrSocketSession, byte> _sessions = new();

    public MockGeoGuessrSocketSession Open()
    {
        var session = new MockGeoGuessrSocketSession(this);
        _sessions[session] = 0;
        return session;
    }

    /// <summary>Whether a bot session is subscribed to the club's topic.</summary>
    public bool IsLive(Guid clubId) => _sessions.Keys.Any(s => s.IsSubscribedTo(SocketTopics.Club(clubId)));

    /// <summary>Sends <paramref name="code"/> to every session on the club's topic, the payload serialised as GeoGuessr does.</summary>
    public void Publish(Guid clubId, string code, object? payload) =>
        PublishRaw(clubId, code, payload is null ? null : JsonSerializer.Serialize(payload, PayloadOptions));

    /// <summary>Like <see cref="Publish"/>, with the payload's JSON as given (or none), to try out odd payloads.</summary>
    public void PublishRaw(Guid clubId, string code, string? payloadJson)
    {
        var topic = SocketTopics.Club(clubId);
        var frame = Frame(code, topic, payloadJson);
        foreach (var session in _sessions.Keys.Where(s => s.IsSubscribedTo(topic)))
        {
            session.Deliver(frame);
        }
    }

    /// <summary>Ends the club's sessions as GeoGuessr would: with a close code, or (null) without a closing handshake.</summary>
    /// <returns>How many sessions were dropped.</returns>
    public int Drop(Guid clubId, int? closeCode, string? description)
    {
        var topic = SocketTopics.Club(clubId);
        var dropped = 0;
        foreach (var session in _sessions.Keys.Where(s => s.IsSubscribedTo(topic)))
        {
            session.CloseFromServer(closeCode, description);
            dropped++;
        }

        return dropped;
    }

    internal void Subscribe(MockGeoGuessrSocketSession session, string? topic)
    {
        var known = (ClubOf(topic, ClubTopicPrefix) ?? ClubOf(topic, ChatTopicPrefix)) is { } clubId && store.Clubs.ContainsKey(clubId);
        if (known)
        {
            session.AddTopic(topic!);
            session.Deliver(Frame("Subscribed", topic, """{"level":1}"""));
        }
        else
        {
            session.Deliver(Frame("SubscribeDenied", topic, null));
        }
    }

    /// <summary>
    /// A message written into a club's chat: stored as GeoGuessr would (cut at 300 characters) and,
    /// unless the club's echoes are dropped, sent back to every socket on the chat's topic.
    /// </summary>
    internal void ReceiveChat(string? topic, string? text)
    {
        if (ClubOf(topic, ChatTopicPrefix) is not { } clubId || !store.Clubs.ContainsKey(clubId) || string.IsNullOrEmpty(text))
        {
            return;
        }

        var stored = text.Length > MaxChatLength ? text[..MaxChatLength] : text;
        var echoed = !store.ChatEchoesDropped.GetValueOrDefault(clubId);
        var message = new MockChatMessage(Guid.NewGuid().ToString("N")[..10], stored, DateTimeOffset.UtcNow, echoed);
        store.ClubChats.GetOrAdd(clubId, _ => new ConcurrentQueue<MockChatMessage>()).Enqueue(message);
        if (!echoed)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            id = message.Id,
            payloadType = "Text",
            textPayload = message.Text,
            sourceType = "User",
            sourceId = "mock-bot-account",
            recipientId = clubId.ToString("D"),
            sentAt = message.SentAt,
            roomId = clubId.ToString("D"),
        }, PayloadOptions);
        var frame = Frame("ChatMessage", topic, payload);
        foreach (var session in _sessions.Keys.Where(s => s.IsSubscribedTo(topic!)))
        {
            session.Deliver(frame);
        }
    }

    private const string ClubTopicPrefix = "club:";
    private const string ChatTopicPrefix = "chat:Club:TextMessages:";

    /// <summary>GeoGuessr silently cuts a chat message here.</summary>
    private const int MaxChatLength = 300;

    private static Guid? ClubOf(string? topic, string prefix) =>
        topic is not null && topic.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParse(topic[prefix.Length..], out var clubId)
            ? clubId
            : null;

    internal void Remove(MockGeoGuessrSocketSession session) => _sessions.TryRemove(session, out _);

    private static string Frame(string code, string? topic, string? payloadJson) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["code"] = code,
            ["topic"] = topic,
            ["payload"] = payloadJson,
            ["accessToken"] = "mock-access-token",
            ["client"] = "web",
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
}

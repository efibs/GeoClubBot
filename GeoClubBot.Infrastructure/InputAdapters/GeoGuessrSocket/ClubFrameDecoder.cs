using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using UseCases.UseCases.ClubEvents;

namespace Infrastructure.InputAdapters.GeoGuessrSocket;

/// <summary>What a frame received on a club's socket means to the bot.</summary>
public abstract record DecodedClubFrame
{
    /// <param name="FieldSet">The payload's property names (nested ones as <c>parent.child</c>), for the field-set log.</param>
    /// <param name="PayloadUnusable">A member event whose payload lacked what the bot needs; <paramref name="Event"/> is then a roster re-read.</param>
    public sealed record Push(string Code, ClubPushEvent Event, string? FieldSet, bool PayloadUnusable = false) : DecodedClubFrame;

    public sealed record Subscribed(string? Topic) : DecodedClubFrame;

    public sealed record SubscribeDenied(string? Topic) : DecodedClubFrame;

    /// <summary>A message in the club's chat, whoever wrote it: the bot's own confirm a forward.</summary>
    public sealed record ChatMessage(string Text, string? MessageId, string? SenderId) : DecodedClubFrame;

    /// <summary>A known frame the bot has no use for, or one about another topic.</summary>
    public sealed record Ignored(string Code) : DecodedClubFrame;

    /// <summary>A code nobody has seen yet, logged so protocol changes get noticed.</summary>
    public sealed record Unknown(string Code) : DecodedClubFrame;

    public sealed record Malformed : DecodedClubFrame;
}

/// <summary>
/// Turns frames of a club's topic into <see cref="ClubPushEvent"/>s. A frame is
/// <c>{code, topic, payload, accessToken, client, timestamp}</c> with the payload a JSON document
/// encoded as a string. <c>accessToken</c> is never read.
/// </summary>
/// <remarks>
/// The payload shapes come from GeoGuessr's own client code and have not been observed live (see
/// <c>Documentation/GeoGuessrSocket.md</c>). Hence each member payload is checked for every field the
/// bot stores, and one that lacks any becomes a roster re-read instead of a guess.
/// </remarks>
public static class ClubFrameDecoder
{
    public const string MemberJoined = "ClubMemberJoined";
    public const string MemberLeft = "ClubMemberLeft";
    public const string ClubUpdated = "ClubUpdated";
    public const string MissionBoardUpdated = "ClubMissionBoardUpdated";
    public const string ChatMessageCode = "ChatMessage";

    /// <summary>Codes the site's client handles that the bot has no use for.</summary>
    private static readonly FrozenSet<string> IgnoredCodes = FrozenSet.ToFrozenSet(
    [
        "ClubMemberRoleChanged", "ClubMemberCameOnline", "ClubMemberWentOffline", "ClubMemberActivityChanged",
        "MissionBoardProgressed", "ChatMessage", "ChatPollMessage", "ChatReaction", "PollUpdated", "PollDeleted",
        "FeedReactionAdded", "AccountUpdate", "BadgesUnlocked", "NotificationUpdate", "FriendsUpdated",
        "NewFriendRequest", "FriendCameOnline", "FriendWentOffline", "StatusActivityChanged",
    ]);

    public static DecodedClubFrame Decode(string text, Guid clubId)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("code", out var codeElement)
                || codeElement.ValueKind != JsonValueKind.String
                || codeElement.GetString() is not { Length: > 0 } code)
            {
                return new DecodedClubFrame.Malformed();
            }

            var topic = root.TryGetProperty("topic", out var topicElement) && topicElement.ValueKind == JsonValueKind.String
                ? topicElement.GetString()
                : null;

            switch (code)
            {
                case "Subscribed":
                    return new DecodedClubFrame.Subscribed(topic);
                case "SubscribeDenied":
                    return new DecodedClubFrame.SubscribeDenied(topic);
                case MemberJoined or MemberLeft or ClubUpdated or MissionBoardUpdated
                    when !string.Equals(topic, SocketTopics.Club(clubId), StringComparison.OrdinalIgnoreCase):
                    return new DecodedClubFrame.Ignored(code);
                case ChatMessageCode when string.Equals(topic, SocketTopics.ClubChat(clubId), StringComparison.OrdinalIgnoreCase):
                    return DecodeChatMessage(root);
            }

            var order = new PushOrder(ReadTimestamp(root), 0);
            using var payload = ReadPayload(root);
            var payloadRoot = payload?.RootElement;

            return code switch
            {
                MemberJoined => DecodeJoined(clubId, order, payloadRoot),
                MemberLeft => DecodeLeft(clubId, order, payloadRoot),
                ClubUpdated => DecodeClubUpdated(clubId, order, payloadRoot),
                MissionBoardUpdated => new DecodedClubFrame.Push(code, new ClubMissionBoardUpdatedPush(clubId, order), FieldSet(payloadRoot)),
                _ => IgnoredCodes.Contains(code) ? new DecodedClubFrame.Ignored(code) : new DecodedClubFrame.Unknown(code),
            };
        }
        catch (JsonException)
        {
            return new DecodedClubFrame.Malformed();
        }
    }

    /// <summary>
    /// The payload is a chat message: <c>{id, payloadType, textPayload, sourceType, sourceId,
    /// recipientId, sentAt, roomId}</c>, as observed for direct messages. One without text (a poll,
    /// a picture) is of no use to the bot.
    /// </summary>
    private static DecodedClubFrame DecodeChatMessage(JsonElement root)
    {
        using var payload = ReadPayload(root);
        if (payload?.RootElement is { ValueKind: JsonValueKind.Object } message
            && message.TryGetProperty("textPayload", out var text) && text.ValueKind == JsonValueKind.String
            && text.GetString() is { } body)
        {
            return new DecodedClubFrame.ChatMessage(body, ReadString(message, "id"), ReadString(message, "sourceId"));
        }

        return new DecodedClubFrame.Ignored(ChatMessageCode);
    }

    /// <summary>The payload is an item of <c>/v4/clubs/{id}/members</c>: the bot needs its user's id and nick, its XP and when they joined.</summary>
    private static DecodedClubFrame DecodeJoined(Guid clubId, PushOrder order, JsonElement? payload)
    {
        var fields = FieldSet(payload);
        if (payload is { ValueKind: JsonValueKind.Object } member
            && member.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object
            && ReadString(user, "userId") is { } userId
            && ReadString(user, "nick") is { } nick
            && ReadInt(member, "xp") is { } xp
            && ReadDate(member, "joinedAt") is { } joinedAt)
        {
            return new DecodedClubFrame.Push(MemberJoined, new ClubMemberJoinedPush(clubId, order, userId, nick, xp, joinedAt), fields);
        }

        return Unusable(MemberJoined, clubId, order, fields);
    }

    private static DecodedClubFrame DecodeLeft(Guid clubId, PushOrder order, JsonElement? payload)
    {
        var fields = FieldSet(payload);
        return payload is { ValueKind: JsonValueKind.Object } left && ReadString(left, "userId") is { } userId
            ? new DecodedClubFrame.Push(MemberLeft, new ClubMemberLeftPush(clubId, order, userId), fields)
            : Unusable(MemberLeft, clubId, order, fields);
    }

    /// <summary>A partial club: whatever it doesn't carry is unchanged, so a payload without level or name changes nothing.</summary>
    private static DecodedClubFrame DecodeClubUpdated(Guid clubId, PushOrder order, JsonElement? payload)
    {
        int? level = null;
        string? name = null;
        if (payload is { ValueKind: JsonValueKind.Object } club)
        {
            level = ReadInt(club, "level");
            name = ReadString(club, "name");
        }

        return new DecodedClubFrame.Push(ClubUpdated, new ClubUpdatedPush(clubId, order, level, name), FieldSet(payload));
    }

    private static DecodedClubFrame.Push Unusable(string code, Guid clubId, PushOrder order, string? fields) =>
        new(code, new ClubRosterChangedPush(clubId, order), fields, PayloadUnusable: true);

    /// <summary>The payload is a JSON document encoded as a string; anything else (null included) is no payload.</summary>
    private static JsonDocument? ReadPayload(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.String
            || payload.GetString() is not { Length: > 0 } json)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>GeoGuessr's timestamp, as epoch milliseconds (or seconds) or an ISO string; null when absent or unreadable.</summary>
    private static DateTimeOffset? ReadTimestamp(JsonElement root)
    {
        if (!root.TryGetProperty("timestamp", out var timestamp))
        {
            return null;
        }

        switch (timestamp.ValueKind)
        {
            case JsonValueKind.Number when timestamp.TryGetInt64(out var epoch) && epoch > 0:
                // Ten-digit values are seconds; GeoGuessr's own are most likely milliseconds.
                try
                {
                    return epoch >= 100_000_000_000
                        ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                        : DateTimeOffset.FromUnixTimeSeconds(epoch);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }

            case JsonValueKind.String:
                return ParseDate(timestamp.GetString());

            default:
                return null;
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null,
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? ParseDate(value.GetString())
            : null;

    private static DateTimeOffset? ParseDate(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    /// <summary>
    /// The payload's property names, sorted, with an object's own properties as <c>parent.child</c>:
    /// enough to see what GeoGuessr really sends without logging any value.
    /// </summary>
    private static string? FieldSet(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } root)
        {
            return payload is { } other ? other.ValueKind.ToString().ToLowerInvariant() : null;
        }

        var names = new List<string>();
        foreach (var property in root.EnumerateObject())
        {
            names.Add(property.Name);
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                names.AddRange(property.Value.EnumerateObject().Select(child => $"{property.Name}.{child.Name}"));
            }
        }

        names.Sort(StringComparer.Ordinal);
        return string.Join(",", names);
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeoClubBot.ApiProbe;

/// <summary>
/// The socket counterpart of <c>ReadOnlyGuardHandler</c>: every frame the probe would send
/// passes through here first. Only two are allowed — subscribing to a club's topic, and the site's
/// heartbeat — so the probe can listen but never chat, react, vote or do anything else in a club.
/// </summary>
public static partial class ReadOnlySocketGuard
{
    /// <exception cref="InvalidOperationException">The frame is anything but a club subscription or the heartbeat.</exception>
    public static void EnsureAllowed(string frame)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(frame);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Refusing to send a socket frame that isn't JSON.", ex);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Refused("it isn't an object");
        }

        var names = root.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var code = root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
            ? codeElement.GetString()
            : null;

        switch (code)
        {
            case "HeartBeat" when names.SetEquals(["code"]):
                return;

            case "Subscribe" when names.SetEquals(["code", "topic", "client"])
                                  && root.GetProperty("topic").ValueKind == JsonValueKind.String
                                  && ClubTopic().IsMatch(root.GetProperty("topic").GetString()!)
                                  && root.GetProperty("client").ValueKind == JsonValueKind.String:
                return;

            default:
                throw Refused($"only a club subscription and the heartbeat may be sent, not '{code ?? "(no code)"}' with {string.Join(", ", names.Order())}");
        }
    }

    private static InvalidOperationException Refused(string why) =>
        new($"Refusing to send a socket frame: {why}. The probe only listens.");

    [GeneratedRegex("^club:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClubTopic();
}

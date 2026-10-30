using UseCases.OutputPorts.AI;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

/// <summary>
/// The <c>/ai status</c> line for the image relay's public URL.
///
/// Exists because the relay's links failed silently on 2026-10-22..24: the tunnel's public DNS record
/// was gone, nothing in the bot errored, and the only symptom was answers rated down for a missing
/// picture. One line in the status command makes that state visible on demand.
/// </summary>
public static class AiImageRelayStatusFormatter
{
    public const string FieldName = "Picture links";

    /// <summary>
    /// Room for the resolver's message, well inside Discord's 1024-character field limit even with a
    /// long base URL around it.
    /// </summary>
    private const int MaxDetailLength = 300;

    public static string Describe(ImageRelayReachability? reachability) => reachability switch
    {
        null => "unknown",
        { Status: ImageRelayReachabilityStatus.NotConfigured } =>
            "not configured — set `AI:ImageRelay:PublicBaseUrl`; pictures on disk are still uploaded",
        { Status: ImageRelayReachabilityStatus.Reachable } =>
            // Said as "from the bot's host" because that is all it proves: a host that resolves the name
            // privately can pass while the public record — the one Discord uses — is gone.
            $"✅ `{reachability.PublicBaseUrl}` answered `/health` from the bot's host{Parenthesised(reachability.Detail)}",
        _ =>
            $"❌ `{reachability.PublicBaseUrl}` is unreachable{Colon(reachability.Detail)} — "
            + "pictures on disk are still uploaded, but linked ones will not load"
    };

    private static string Parenthesised(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({Truncate(detail)})";

    private static string Colon(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {Truncate(detail)}";

    private static string Truncate(string detail)
    {
        var flat = detail.ReplaceLineEndings(" ").Trim();
        return flat.Length <= MaxDetailLength ? flat : $"{flat[..(MaxDetailLength - 1)]}…";
    }
}

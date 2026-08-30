namespace Configuration;

/// <summary>
/// Serves guide images from our own host so the AI provider can fetch them.
///
/// The provider downloads image URLs server-side, and several guide sites answer unattended clients
/// with 403 — their images are then unusable no matter how relevant. Relaying stores the bytes once
/// during indexing and hands out a URL of ours instead.
/// </summary>
public class AiImageRelayConfiguration
{
    public const string SectionName = "AI:ImageRelay";

    /// <summary>
    /// Public base URL of this bot, e.g. <c>https://host.tailnet.ts.net</c>.
    ///
    /// Must be set for relaying to work, and cannot be inferred: the bot typically sits behind a
    /// tunnel or reverse proxy and only ever sees an internal address. The URL has to be reachable
    /// from the public internet, because the AI provider — not the user's browser — is what fetches it.
    /// Left empty, relaying is off and images from blocked hosts are simply indexed without a picture.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Directory holding the stored images. Mount a volume here so they survive redeploys.</summary>
    public string Directory { get; set; } = "ai-images";

    /// <summary>
    /// Hosts whose images are copied rather than linked. Kept as an explicit list rather than
    /// relaying everything: copying someone's images is a bigger imposition than linking them, and
    /// most hosts serve their own images perfectly well.
    /// </summary>
    public List<string> RelayHosts { get; set; } = ["plonkit.net"];

    /// <summary>Largest image stored, in bytes. Guards the disk against an unexpectedly huge asset.</summary>
    public int MaxImageBytes { get; set; } = 8 * 1024 * 1024;
}

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
    /// from the public internet for the pictures that are linked rather than uploaded, because
    /// Discord's image fetcher — not the user's browser — is what follows the link. A stored URL is
    /// moved onto the current value before it is shown, so changing it does not strand old links.
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

    /// <summary>
    /// Bytes of relayed pictures uploaded with one answer. Pictures past it are linked instead.
    ///
    /// Answers upload the relayed pictures they cite rather than linking them, because a link only
    /// works while Discord can reach <see cref="PublicBaseUrl"/>: on 2026-10-22 the tunnel's public DNS
    /// record disappeared and every relayed picture silently failed to show, while the bytes sat on
    /// this host's disk the whole time. Kept below Discord's default upload limit (10 MiB in a server
    /// without boosts), because a message over it is refused. Zero turns uploading off, so every
    /// picture is linked.
    /// </summary>
    public long MaxUploadBytesPerAnswer { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Largest single picture uploaded with an answer; a bigger one is linked. Discord limits each
    /// file as well as the whole message, so this keeps every file within the per-file limit when
    /// <see cref="MaxUploadBytesPerAnswer"/> is raised to fit several pictures.
    /// </summary>
    public long MaxUploadBytesPerPicture { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// How long <c>/ai status</c> waits for <see cref="PublicBaseUrl"/> to answer its health check.
    /// Short, because the status reply waits for it.
    /// </summary>
    public int ReachabilityTimeoutSeconds { get; set; } = 5;
}

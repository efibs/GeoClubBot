using Configuration;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

/// <summary>How a cited picture reaches Discord, and — when it is linked — why it was not uploaded.</summary>
public enum AiAnswerPictureDelivery
{
    /// <summary>The bytes ride on the answer as a file, so the picture shows whatever Discord can reach.</summary>
    Uploaded,

    /// <summary>Not a relayed image. Its own host serves it, so the stored link is shown unchanged.</summary>
    Linked,

    /// <summary>A relayed image whose file is not on this host's disk.</summary>
    FileMissing,

    /// <summary>Over the per-picture cap, or of a size the store could not tell.</summary>
    TooLarge,

    /// <summary>The answer's upload allowance, or Discord's attachment limit, was already spent.</summary>
    OverAllowance,

    /// <summary>Planned as an upload, but Discord refused the message that carried it.</summary>
    UploadRefused
}

/// <summary>A relayed picture as found on this host's disk, before anything is uploaded.</summary>
/// <param name="ContentType">The type the relay serves it as, which also names the uploaded file's extension.</param>
/// <param name="Length">Null when the store cannot tell without reading the whole file.</param>
public sealed record StoredPicture(string ContentType, long? Length);

/// <param name="FileName">The attachment's name; the embed shows it as <c>attachment://{FileName}</c>.</param>
/// <param name="StoredName">The relay's name for the bytes, to read them back with.</param>
public sealed record AiAnswerUpload(string FileName, string StoredName, long Length);

/// <param name="LinkUrl">
/// Where the picture is when it is not uploaded. A relayed picture's link is already moved onto the
/// public base URL configured now, because the host it was indexed under may no longer resolve.
/// </param>
/// <param name="Upload">The file carrying the picture, or null when it is linked.</param>
public sealed record AiAnswerPicture(
    AiAnswerImage Image,
    AiAnswerPictureDelivery Delivery,
    string LinkUrl,
    AiAnswerUpload? Upload)
{
    /// <summary>What the embed's image points at: the uploaded file when there is one, else the link.</summary>
    public string EmbedImageUrl => Upload is { } upload ? $"attachment://{upload.FileName}" : LinkUrl;
}

/// <param name="Pictures">One per picture shown, in the answer's number order.</param>
public sealed record AiAnswerPicturePlan(IReadOnlyList<AiAnswerPicture> Pictures)
{
    /// <summary>The files to attach, once each — two pictures holding the same bytes share one.</summary>
    public IReadOnlyList<AiAnswerUpload> Uploads =>
        [.. Pictures.Select(picture => picture.Upload).OfType<AiAnswerUpload>().Distinct()];

    /// <summary>
    /// The same pictures with every upload turned back into its link: what is posted when Discord
    /// refuses the message carrying the files, so the answer still arrives.
    /// </summary>
    public AiAnswerPicturePlan WithoutUploads() =>
        new([.. Pictures.Select(picture => picture.Upload is null
            ? picture
            : picture with { Delivery = AiAnswerPictureDelivery.UploadRefused, Upload = null })]);
}

/// <summary>
/// Decides which of an answer's pictures are uploaded with it and which are linked.
///
/// Relayed pictures are uploaded because their link only works while Discord can reach the bot's
/// public base URL. On 2026-10-22..24 the tunnel's public DNS record disappeared, and every relayed
/// picture — 92% of the indexed ones — silently failed to show while its bytes sat on this host's
/// disk; users rated the answers down for the missing picture the model had cited. Pictures from
/// other hosts are still linked: those hosts serve them, and copying is a bigger imposition.
///
/// Pure, so the choices (and every fallback) are unit-tested; reading and sending the files is the
/// caller's.
/// </summary>
public static class AiAnswerPicturePlanner
{
    /// <summary>Discord shows at most ten embeds, and takes at most ten attachments, per message.</summary>
    public const int MaxPicturesPerMessage = 10;

    /// <summary>
    /// The stored names worth reading from disk: the relayed pictures among those that will be shown,
    /// once each.
    /// </summary>
    public static IReadOnlyList<string> RelayedNames(IReadOnlyList<AiAnswerImage> images) =>
        [.. images
            .Take(MaxPicturesPerMessage)
            .Select(image => RelayedImageUrl.TryReadName(image.ImageUrl, out var name) ? name : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];

    /// <param name="stored">The relayed pictures found on disk, by stored name; a name missing here is not on disk.</param>
    /// <param name="settings">Supplies the current public base URL and the upload caps.</param>
    public static AiAnswerPicturePlan Plan(
        IReadOnlyList<AiAnswerImage> images,
        IReadOnlyDictionary<string, StoredPicture> stored,
        AiImageRelayConfiguration settings)
    {
        var pictures = new List<AiAnswerPicture>();
        var uploadsByName = new Dictionary<string, AiAnswerUpload>(StringComparer.Ordinal);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long uploadedBytes = 0;

        foreach (var image in images.Take(MaxPicturesPerMessage))
        {
            if (!RelayedImageUrl.TryReadName(image.ImageUrl, out var name))
            {
                pictures.Add(new AiAnswerPicture(image, AiAnswerPictureDelivery.Linked, image.ImageUrl, Upload: null));
                continue;
            }

            var link = RelayedImageUrl.Rebase(image.ImageUrl, settings.PublicBaseUrl);

            // The same bytes cited twice under two addresses — an old host and the current one — are
            // sent once and shown twice, rather than costing the allowance a second time.
            if (uploadsByName.TryGetValue(name, out var shared))
            {
                pictures.Add(new AiAnswerPicture(image, AiAnswerPictureDelivery.Uploaded, link, shared));
                continue;
            }

            var delivery = Decide(stored.GetValueOrDefault(name), uploadedBytes, uploadsByName.Count, settings);
            if (delivery != AiAnswerPictureDelivery.Uploaded)
            {
                pictures.Add(new AiAnswerPicture(image, delivery, link, Upload: null));
                continue;
            }

            var file = stored[name];
            var upload = new AiAnswerUpload(NameFile(image, name, file.ContentType, fileNames), name, file.Length!.Value);

            uploadsByName[name] = upload;
            uploadedBytes += upload.Length;
            pictures.Add(new AiAnswerPicture(image, AiAnswerPictureDelivery.Uploaded, link, upload));
        }

        return new AiAnswerPicturePlan(pictures);
    }

    /// <summary>
    /// Pictures are considered in number order, so when the allowance runs out it is the
    /// later-cited ones that are linked. A picture too big for what is left does not stop a smaller,
    /// later one from fitting.
    /// </summary>
    private static AiAnswerPictureDelivery Decide(
        StoredPicture? file,
        long uploadedBytes,
        int uploadCount,
        AiImageRelayConfiguration settings)
    {
        if (file is null || file.Length == 0)
        {
            return AiAnswerPictureDelivery.FileMissing;
        }

        if (file.Length is not { } length || length > settings.MaxUploadBytesPerPicture)
        {
            return AiAnswerPictureDelivery.TooLarge;
        }

        return uploadCount >= MaxPicturesPerMessage || uploadedBytes + length > settings.MaxUploadBytesPerAnswer
            ? AiAnswerPictureDelivery.OverAllowance
            : AiAnswerPictureDelivery.Uploaded;
    }

    /// <summary>
    /// Named by the picture's number — and its place among its excerpt's pictures when there are
    /// several, as its embed is titled — so a downloaded file still says which "[n]" it was. The
    /// extension follows the served type, because Discord decides from the name whether an attachment
    /// is an image it can show in an embed.
    /// </summary>
    private static string NameFile(AiAnswerImage image, string storedName, string contentType, HashSet<string> taken)
    {
        var extension = RelayedImageUrl.ExtensionFor(contentType) ?? Path.GetExtension(storedName);
        var stem = image.Count > 1 ? $"picture-{image.Marker}-{image.Position}" : $"picture-{image.Marker}";
        var fileName = $"{stem}{extension}";

        for (var suffix = 2; !taken.Add(fileName); suffix++)
        {
            fileName = $"{stem}-{suffix}{extension}";
        }

        return fileName;
    }
}

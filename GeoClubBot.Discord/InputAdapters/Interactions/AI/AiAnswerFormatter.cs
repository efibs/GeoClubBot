using Discord;
using Extensions;
using UseCases.UseCases.AI.Conversations;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

/// <param name="MessageChunks">Answer split to Discord's per-message limit; post in order.</param>
/// <param name="Embeds">The pictures, posted beneath the final chunk.</param>
/// <param name="Uploads">
/// Files the final chunk must carry, because its embeds refer to them by name. Embeds posted without
/// them show a broken picture, so the two travel together.
/// </param>
public sealed record AiAnswerRendering(
    IReadOnlyList<string> MessageChunks,
    IReadOnlyList<Embed> Embeds,
    IReadOnlyList<AiAnswerUpload> Uploads);

/// <summary>
/// Turns an answer into the messages, embeds and uploads Discord will actually show.
///
/// Pure, so the layout is snapshot-tested rather than eyeballed in a live server.
/// </summary>
public static class AiAnswerFormatter
{
    private const int DiscordMessageLimit = 2000;

    /// <summary>Discord rejects an embed whose title is longer, and a guide's section path can be.</summary>
    private const int MaxEmbedTitleLength = 256;

    /// <param name="pictures">
    /// How each picture is delivered, from <see cref="AiAnswerPicturePlanner"/>. An uploaded one keeps
    /// its numbered, linked embed; only the image inside it points at the attachment.
    /// </param>
    public static AiAnswerRendering Render(AiAnswer answer, AiAnswerPicturePlan pictures)
    {
        var body = answer.Text.Trim();
        if (body.Length == 0)
        {
            body = "_(the model returned nothing to show)_";
        }

        var footer = BuildFooter(answer);
        var full = footer.Length == 0 ? body : $"{body}\n\n{footer}";

        var chunks = full.SplitAtCharWithLimit("\n", DiscordMessageLimit).ToList();

        var shown = new AiAnswerPicturePlan(
            [.. pictures.Pictures.Take(AiAnswerPicturePlanner.MaxPicturesPerMessage)]);

        var embeds = shown.Pictures
            .GroupBy(picture => picture.Image.SourceUrl, StringComparer.Ordinal)
            .SelectMany(gallery =>
            {
                var title = BuildTitle([.. gallery.Select(picture => picture.Image)]);
                return gallery.Select(picture => new EmbedBuilder()
                    .WithImageUrl(picture.EmbedImageUrl)
                    // Linking the embed to its source makes every shown image a citation rather than a
                    // decoration, and credits the guide it came from.
                    .WithUrl(picture.Image.SourceUrl)
                    // Numbered like the prose, so "[2]" in a sentence leads to the picture that shows it.
                    .WithTitle(title)
                    .Build());
            })
            .ToList();

        return new AiAnswerRendering(chunks, embeds, shown.Uploads);
    }

    /// <summary>
    /// The title of every embed linking to one source: "[2] Title", "[2] Title (3 pictures)" when it
    /// shows several, "[1][3] Title (2 pictures)" when several cited excerpts share the link.
    ///
    /// Discord shows embeds sharing a link as one, under the first one's title, with all their pictures
    /// in a grid. So they are posted together, and each carries the title of the whole group: a
    /// position like "(1/3)" would head a grid of three, and the "(2/3)" and "(3/3)" it promises never
    /// appear. The count is kept when a long title is cut.
    /// </summary>
    private static string BuildTitle(IReadOnlyList<AiAnswerImage> gallery)
    {
        var name = gallery.Select(image => image.Title).FirstOrDefault(title => !string.IsNullOrWhiteSpace(title)) ?? "Guide image";
        var markers = string.Concat(gallery.Select(image => image.Marker).Distinct().Order().Select(marker => $"[{marker}]"));
        var count = gallery.Count > 1 ? $" ({gallery.Count} pictures)" : string.Empty;

        return Truncate($"{markers} {name}", MaxEmbedTitleLength - count.Length) + count;
    }

    /// <summary>
    /// Subtext credits the model that answered, so a drop in quality can be traced to a specific one
    /// rather than blamed on "the bot".
    /// </summary>
    private static string BuildFooter(AiAnswer answer)
    {
        var lines = new List<string>();

        // Named and clickable, because "[1]" on its own tells the reader nothing and leads nowhere.
        // Masked links render here: Discord blocks them in messages people type, to stop a friendly
        // label hiding a hostile URL, but honours them in what a bot posts through the API.
        //
        // The number stays outside the link so it still reads as the anchor for the "[1]" in the
        // prose, and so the label cannot nest brackets inside a masked link. Cited pictures are listed
        // too, marked as such, so the numbers run 1, 2, 3 without a gap where a picture sits.
        if (answer.Sources.Count > 0)
        {
            // An unnumbered list is the model citing nothing and the guides being credited for it.
            // Said outright, because a reader otherwise takes it for backing the answer never claimed —
            // "I can't confirm if it is actually correct since there is no source" was the complaint.
            if (answer.Sources.All(source => source.Marker is null))
            {
                lines.Add("-# This answer cites no guide. Closest matches:");
            }

            foreach (var source in answer.Sources)
            {
                var picture = source.IsImage ? "🖼️ " : string.Empty;

                lines.Add(source.Marker is { } marker
                    ? $"-# [{marker}] {picture}[{source.Label}]({source.Url})"
                    : $"-# {picture}[{source.Label}]({source.Url})");
            }
        }

        if (answer.IsLongThread)
        {
            lines.Add("-# This thread is getting long — mention me in a new message to start fresh.");
        }

        if (!string.IsNullOrWhiteSpace(answer.ModelUsed))
        {
            lines.Add($"-# via {answer.ModelUsed}");
        }

        return string.Join("\n", lines);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : $"{value[..(maxLength - 1)]}…";
}

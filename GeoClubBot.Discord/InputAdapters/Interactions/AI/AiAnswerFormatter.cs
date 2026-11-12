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

    /// <summary>
    /// Pictures Discord shows in one gallery. Embeds sharing a link are drawn as one, and a fifth
    /// picture under that link is not drawn at all.
    /// </summary>
    private const int MaxPicturesPerGallery = 4;

    /// <summary>
    /// The text Discord takes in one message's embeds, their titles and descriptions added up. Past it
    /// the whole message is refused, answer and all.
    /// </summary>
    private const int MaxEmbedTextLength = 6000;

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

        return new AiAnswerRendering(chunks, BuildEmbeds(shown.Pictures), shown.Uploads);
    }

    /// <summary>
    /// One gallery per cited excerpt: its pictures under its number, its heading and what the guide
    /// says of them.
    ///
    /// Discord draws embeds that share a link as one embed, under the first one's title, with at most
    /// <see cref="MaxPicturesPerGallery"/> pictures. Pictures used to be grouped by their guide's link
    /// for that reason, and titled as the group they became: five clues cited from one Google Doc stood
    /// under one title, "[2][3][4][5][6] 28 Secret US Metas (5 pictures)" — "it is hard to understand
    /// which image is which clue" — and the fifth, the Ohio gap, was not shown at all (2026-11-09).
    ///
    /// So each excerpt's pictures get a link of their own: the guide's, told apart from an earlier
    /// gallery's by a query parameter the guide's site ignores, placed before the fragment so the link
    /// still opens at the section it names. An excerpt with more pictures than one gallery holds takes
    /// a second.
    /// </summary>
    private static List<Embed> BuildEmbeds(IReadOnlyList<AiAnswerPicture> pictures)
    {
        var galleries = new List<(string Link, string Title, AiAnswerPicture[] Pictures, string? Caption)>();
        var galleriesPerLink = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var excerpt in pictures.GroupBy(picture => picture.Image.Marker))
        {
            var all = excerpt.ToList();
            var first = all[0].Image;
            var shownBefore = 0;

            foreach (var part in all.Chunk(MaxPicturesPerGallery))
            {
                var ordinal = galleriesPerLink[first.SourceUrl] = galleriesPerLink.GetValueOrDefault(first.SourceUrl) + 1;

                galleries.Add((
                    DistinctLink(first.SourceUrl, ordinal),
                    // Numbered like the prose, so "[2]" in a sentence leads to the picture that shows it.
                    BuildTitle(first, all.Count, shownBefore, part.Length),
                    part,
                    // Said once, under the excerpt's first gallery: what these pictures are of.
                    shownBefore == 0 && !string.IsNullOrWhiteSpace(first.Caption) ? EscapeFormatting(first.Caption) : null));

                shownBefore += part.Length;
            }
        }

        // Every embed of a gallery carries its title, and Discord counts each one. What the titles
        // leave of the message's allowance goes to the captions in reading order; one that no longer
        // fits is left out whole, and its pictures still stand under their heading.
        var left = MaxEmbedTextLength - galleries.Sum(gallery => gallery.Title.Length * gallery.Pictures.Length);

        var embeds = new List<Embed>();
        foreach (var (link, title, part, caption) in galleries)
        {
            var captioned = caption is not null && caption.Length <= left;
            if (captioned)
            {
                left -= caption!.Length;
            }

            for (var index = 0; index < part.Length; index++)
            {
                var embed = new EmbedBuilder()
                    .WithImageUrl(part[index].EmbedImageUrl)
                    // Linking the embed to its source makes every shown image a citation rather than a
                    // decoration, and credits the guide it came from.
                    .WithUrl(link)
                    .WithTitle(title);

                if (index == 0 && captioned)
                {
                    embed.WithDescription(caption);
                }

                embeds.Add(embed.Build());
            }
        }

        return embeds;
    }

    /// <summary>
    /// A gallery's title: "[2] Heading", "[2] Heading (3 pictures)" when it shows several, and
    /// "[2] Heading (pictures 5–7 of 7)" for what did not fit the gallery before it. Every embed of a
    /// gallery carries it, since Discord shows the first one's. The count is kept when a long title is cut.
    /// </summary>
    private static string BuildTitle(AiAnswerImage image, int total, int shownBefore, int shownHere)
    {
        var name = string.IsNullOrWhiteSpace(image.Title) ? "Guide image" : image.Title;

        var count = total <= 1
            ? string.Empty
            : shownHere == total
                ? $" ({total} pictures)"
                : $" (pictures {shownBefore + 1}–{shownBefore + shownHere} of {total})";

        return Truncate($"[{image.Marker}] {name}", MaxEmbedTitleLength - count.Length) + count;
    }

    /// <summary>
    /// A guide's words as plain text: the characters Discord reads as formatting are escaped, so an
    /// underscore in a name or a stray asterisk cannot set half a caption in italics. Only those —
    /// Discord.Net's own sanitiser escapes every full stop and colon as well.
    /// </summary>
    private static string EscapeFormatting(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length + 8);
        foreach (var character in text)
        {
            if (character is '\\' or '*' or '_' or '~' or '`' or '|')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The guide's link as the first gallery showing it has it, and with <c>_=n</c> added to its query
    /// for the nth: another address to Discord, the same page to the guide's site.
    /// </summary>
    private static string DistinctLink(string url, int ordinal)
    {
        if (ordinal <= 1)
        {
            return url;
        }

        var fragmentStart = url.IndexOf('#', StringComparison.Ordinal);
        var page = fragmentStart < 0 ? url : url[..fragmentStart];
        var fragment = fragmentStart < 0 ? string.Empty : url[fragmentStart..];

        return $"{page}{(page.Contains('?', StringComparison.Ordinal) ? '&' : '?')}_={ordinal}{fragment}";
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

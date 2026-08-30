using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Entities;
using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Conversations;

/// <param name="Marker">The <c>[image N]</c> token the model is told to cite this image by.</param>
public sealed record CitedImage(int Marker, string ImageUrl, string SourceUrl, string? Title);

/// <param name="Marker">The <c>[N]</c> token this excerpt was offered under.</param>
/// <param name="Label">What the model saw as the excerpt's heading, so the reply names it the same way.</param>
public sealed record OfferedExcerpt(int Marker, string SourceUrl, string Label);

/// <param name="Excerpts">Every excerpt offered, so a marker in the answer can be resolved to its source.</param>
public sealed record AiPrompt(
    IReadOnlyList<AiChatMessage> Messages,
    IReadOnlyList<CitedImage> Images,
    IReadOnlyList<OfferedExcerpt> Excerpts);

/// <summary>
/// Assembles the messages sent to the model, and interprets the citation markers that come back.
///
/// Pure so prompt shape and marker handling can be tested without a provider. Retrieval happens before
/// the call rather than through tool-calling: requiring tool support would exclude most of the free
/// models this feature depends on, and the weaker ones invoke tools unreliably.
/// </summary>
public static partial class AiPromptBuilder
{
    /// <summary>Excerpts are numbered from one so the model's citations read naturally.</summary>
    private const int FirstMarker = 1;

    /// <summary>
    /// Leads with the image affordance rather than qualifying it, because models otherwise fall back
    /// on the assistant's default posture and answer "I can't display images" — which is both wrong
    /// here and the exact opposite of the feature. Citing is framed as the normal thing to do for a
    /// visual game, not as an exception for when prose falls short.
    /// </summary>
    public const string SystemPrompt = """
        You are a GeoGuessr assistant for a club Discord server. You help players identify countries
        and regions from visual clues: road markings, bollards, utility poles, licence plates,
        architecture, vegetation, scripts and the Google car itself.

        You can show pictures, and you should. Some excerpts below are images, labelled [image 1],
        [image 2] and so on. Writing that marker anywhere in your answer attaches that picture to your
        reply for the user to see. This is a visual game and a picture usually settles a question
        faster than any description of it, so cite an image whenever one is relevant — not only when
        words fail you. Describe what it shows. Never say you are unable to display or retrieve
        images, and never claim to be showing one without writing its marker.

        Answer from the guide excerpts whenever they are relevant, citing text excerpts by their plain
        marker, like [1]. Each marker you use is turned into a named, clickable link under your answer,
        so cite the marker and never write a URL yourself. If the excerpts do not cover the question,
        say so plainly and answer from your own knowledge, making clear which part is not from the
        guides. Never invent a source or a marker.

        Keep answers short and concrete. Prefer specific, checkable clues over general advice.
        """;

    /// <summary>
    /// Builds the full message list: system prompt, replayed history, then the current question with
    /// its retrieved excerpts.
    /// </summary>
    public static AiPrompt Build(
        ConversationContext context,
        string question,
        IReadOnlyList<string> attachmentImageUrls,
        IReadOnlyList<KnowledgeHit> hits)
    {
        var messages = new List<AiChatMessage> { AiChatMessage.System(SystemPrompt) };

        if (context.WasTrimmed)
        {
            // Told explicitly, so the model does not treat a truncated thread as the whole story.
            messages.Add(AiChatMessage.System("Earlier turns in this thread were trimmed for length."));
        }

        foreach (var turn in context.Turns)
        {
            messages.Add(turn.Role == AiTurnRole.Assistant
                ? AiChatMessage.Assistant(turn.Content)
                // Several people can share one branch, so each question is attributed; otherwise the
                // model reads a group discussion as one person contradicting themselves.
                : AiChatMessage.User(FormatUserTurn(turn), turn.ImageUrls));
        }

        var (excerpts, images, offered) = FormatExcerpts(hits);

        var prompt = new StringBuilder();
        if (excerpts.Length > 0)
        {
            prompt.AppendLine("Guide excerpts:").AppendLine(excerpts).AppendLine();
        }
        else
        {
            prompt.AppendLine("No guide excerpts matched this question.").AppendLine();
        }

        // Repeated next to the question rather than left to the system prompt alone: the markers are
        // already in the excerpt labels, but a weak model reads them as decoration unless it is told,
        // at the point of answering, that they are an action available to it.
        if (images.Count > 0)
        {
            prompt.Append("Images you can attach: ")
                .AppendLine(string.Join(", ", images.Select(image => $"[image {image.Marker}]")))
                .AppendLine("Cite the ones that help and they will be attached to your reply.")
                .AppendLine();
        }

        prompt.Append("Question: ").Append(question);

        messages.Add(AiChatMessage.User(prompt.ToString(), attachmentImageUrls));

        return new AiPrompt(messages, images, offered);
    }

    private static string FormatUserTurn(ConversationTurnView turn) =>
        $"<@{turn.AuthorDiscordUserId.ToString(CultureInfo.InvariantCulture)}>: {turn.Content}";

    /// <summary>
    /// Renders retrieved chunks as numbered excerpts. Image chunks get a marker the model can cite so
    /// the picture reaches the user, since for a visual game the image is often the actual answer.
    /// </summary>
    private static (string Excerpts, IReadOnlyList<CitedImage> Images, IReadOnlyList<OfferedExcerpt> Offered)
        FormatExcerpts(IReadOnlyList<KnowledgeHit> hits)
    {
        var builder = new StringBuilder();
        var images = new List<CitedImage>();
        var offered = new List<OfferedExcerpt>();

        for (var index = 0; index < hits.Count; index++)
        {
            var hit = hits[index];
            var marker = FirstMarker + index;
            var label = hit.Kind == KnowledgeChunkKind.Image ? $"[image {marker}]" : $"[{marker}]";

            builder.Append(label).Append(' ');

            // The same heading is kept for the reply's source list, so a reader following [1] back
            // finds it named the way the model was shown it.
            var heading = BuildHeading(hit);

            builder.AppendLine(heading);
            builder.AppendLine(hit.Text);
            builder.Append("(source: ").Append(hit.SourceUrl).AppendLine(")").AppendLine();

            offered.Add(new OfferedExcerpt(marker, hit.SourceUrl, heading));

            if (hit.Kind == KnowledgeChunkKind.Image && !string.IsNullOrWhiteSpace(hit.ImageUrl))
            {
                images.Add(new CitedImage(marker, hit.ImageUrl, hit.SourceUrl, hit.Title));
            }
        }

        return (builder.ToString().TrimEnd(), images, offered);
    }

    private static string BuildHeading(KnowledgeHit hit)
    {
        var title = hit.SectionPath ?? hit.Title ?? hit.SourceUrl;

        if (string.IsNullOrWhiteSpace(hit.Country))
        {
            return title;
        }

        var country = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(hit.Country);

        // Section paths are usually rooted at the country already, and "Eswatini · Eswatini > Spotlight"
        // reads as a mistake rather than as emphasis.
        return title.StartsWith(country, StringComparison.OrdinalIgnoreCase) ? title : $"{country} · {title}";
    }

    /// <summary>
    /// Resolves the plain <c>[N]</c> markers an answer cites back to their sources, in citation order.
    ///
    /// The markers are left in the text rather than stripped: they are what ties a sentence to the
    /// entry below it. On their own they say nothing and lead nowhere, which is the whole reason the
    /// resolved list exists.
    /// </summary>
    public static IReadOnlyList<OfferedExcerpt> ResolveCitedSources(
        string answer,
        IReadOnlyList<OfferedExcerpt> offered,
        int maxSources)
    {
        if (offered.Count == 0 || maxSources <= 0)
        {
            return [];
        }

        var byMarker = offered.ToDictionary(excerpt => excerpt.Marker);
        var cited = new List<OfferedExcerpt>();

        foreach (Match match in SourceMarker().Matches(answer))
        {
            if (!int.TryParse(match.Groups["n"].ValueSpan, CultureInfo.InvariantCulture, out var marker)
                || !byMarker.TryGetValue(marker, out var excerpt)
                // A source cited repeatedly is listed once, under the number the prose uses.
                || cited.Any(existing => existing.Marker == excerpt.Marker))
            {
                continue;
            }

            cited.Add(excerpt);
            if (cited.Count == maxSources)
            {
                break;
            }
        }

        return cited;
    }

    /// <summary>
    /// Strips <c>[image N]</c> markers from the answer and returns the images the model actually
    /// cited, in citation order. Markers are removed because they are an instruction to us, not text
    /// the reader should see; the images are attached separately.
    /// </summary>
    public static (string Text, IReadOnlyList<CitedImage> Images) ResolveCitedImages(
        string answer,
        IReadOnlyList<CitedImage> available,
        int maxImages)
    {
        if (available.Count == 0 || maxImages <= 0)
        {
            return (ImageMarker().Replace(answer, string.Empty).Trim(), []);
        }

        var byMarker = available.ToDictionary(image => image.Marker);
        var cited = new List<CitedImage>();

        foreach (Match match in ImageMarker().Matches(answer))
        {
            if (!int.TryParse(match.Groups["n"].ValueSpan, CultureInfo.InvariantCulture, out var marker)
                || !byMarker.TryGetValue(marker, out var image)
                // A model can cite the same image twice; show it once.
                || cited.Any(existing => existing.ImageUrl == image.ImageUrl))
            {
                continue;
            }

            cited.Add(image);
            if (cited.Count == maxImages)
            {
                break;
            }
        }

        return (ImageMarker().Replace(answer, string.Empty).Trim(), cited);
    }

    [GeneratedRegex(@"\[image\s*(?<n>\d+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageMarker();

    /// <summary>Plain citations only — "[image 2]" carries no digits directly after the bracket.</summary>
    [GeneratedRegex(@"\[(?<n>\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex SourceMarker();
}

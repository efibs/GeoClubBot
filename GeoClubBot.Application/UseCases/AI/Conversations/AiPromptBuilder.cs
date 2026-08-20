using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Entities;
using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Conversations;

/// <param name="Marker">The <c>[image N]</c> token the model is told to cite this image by.</param>
public sealed record CitedImage(int Marker, string ImageUrl, string SourceUrl, string? Title);

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

    public const string SystemPrompt = """
        You are a GeoGuessr assistant for a club Discord server. You help players identify countries
        and regions from visual clues: road markings, bollards, utility poles, licence plates,
        architecture, vegetation, scripts and the Google car itself.

        Answer from the guide excerpts provided below whenever they are relevant. Cite the excerpt you
        used with its marker, like [1]. If an excerpt is an image and that image answers the question
        better than any prose, cite it as [image 2] on its own line — the image will be shown to the
        user, so describe what it shows rather than repeating that you are including it.

        If the excerpts do not cover the question, say so plainly and answer from your own knowledge,
        making clear which part is not from the guides. Never invent a source or a marker.

        Keep answers short and concrete. Prefer specific, checkable clues over general advice.
        """;

    /// <summary>
    /// Builds the full message list: system prompt, replayed history, then the current question with
    /// its retrieved excerpts.
    /// </summary>
    public static (IReadOnlyList<AiChatMessage> Messages, IReadOnlyList<CitedImage> Images) Build(
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

        var (excerpts, images) = FormatExcerpts(hits);

        var prompt = new StringBuilder();
        if (excerpts.Length > 0)
        {
            prompt.AppendLine("Guide excerpts:").AppendLine(excerpts).AppendLine();
        }
        else
        {
            prompt.AppendLine("No guide excerpts matched this question.").AppendLine();
        }

        prompt.Append("Question: ").Append(question);

        messages.Add(AiChatMessage.User(prompt.ToString(), attachmentImageUrls));

        return (messages, images);
    }

    private static string FormatUserTurn(ConversationTurnView turn) =>
        $"<@{turn.AuthorDiscordUserId.ToString(CultureInfo.InvariantCulture)}>: {turn.Content}";

    /// <summary>
    /// Renders retrieved chunks as numbered excerpts. Image chunks get a marker the model can cite so
    /// the picture reaches the user, since for a visual game the image is often the actual answer.
    /// </summary>
    private static (string Excerpts, IReadOnlyList<CitedImage> Images) FormatExcerpts(IReadOnlyList<KnowledgeHit> hits)
    {
        var builder = new StringBuilder();
        var images = new List<CitedImage>();

        for (var index = 0; index < hits.Count; index++)
        {
            var hit = hits[index];
            var marker = FirstMarker + index;
            var label = hit.Kind == KnowledgeChunkKind.Image ? $"[image {marker}]" : $"[{marker}]";

            builder.Append(label).Append(' ');

            if (!string.IsNullOrWhiteSpace(hit.Country))
            {
                builder.Append(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(hit.Country)).Append(" · ");
            }

            builder.AppendLine(hit.SectionPath ?? hit.Title ?? hit.SourceUrl);
            builder.AppendLine(hit.Text);
            builder.Append("(source: ").Append(hit.SourceUrl).AppendLine(")").AppendLine();

            if (hit.Kind == KnowledgeChunkKind.Image && !string.IsNullOrWhiteSpace(hit.ImageUrl))
            {
                images.Add(new CitedImage(marker, hit.ImageUrl, hit.SourceUrl, hit.Title));
            }
        }

        return (builder.ToString().TrimEnd(), images);
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
}

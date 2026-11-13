using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;

namespace UseCases.UseCases.AI.ClueOfTheDay;

/// <summary>
/// The clue as a post: shaped like an answer, so it is rendered and uploaded by the same code — the
/// heading, the guide's own words of the picture, the picture as [1], and the guide cited beneath.
/// Pure, so the shape is pinned by a test.
/// </summary>
public static class ClueOfTheDayPost
{
    /// <summary>Written instead of a model's name under the post; the formatter leaves the "via" line out when it is empty.</summary>
    public const string NoModel = "";

    public static AiAnswer Build(KnowledgeHit clue, string headingTemplate, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(clue);

        var guide = string.IsNullOrWhiteSpace(clue.Title) ? "a community guide" : clue.Title.Trim();
        var label = SectionLabel.Clean(clue.SectionPath) is { Length: > 0 } section ? section : guide;

        var heading = (headingTemplate ?? string.Empty).Replace("{{guide}}", guide, StringComparison.OrdinalIgnoreCase).Trim();
        var words = Trim(clue.Text, maxCharacters);

        // Cited as [1], like a picture an answer attaches, so the embed and the source line read alike.
        var text = string.IsNullOrWhiteSpace(heading) ? $"{words} [1]" : $"{heading}\n\n{words} [1]";

        var hasPicture = clue.Kind == KnowledgeChunkKind.Image && !string.IsNullOrWhiteSpace(clue.ImageUrl);
        var caption = PictureCaption.Of(clue.Text, clue.SectionPath, clue.Title);

        return new AiAnswer(
            text,
            hasPicture ? [new AiAnswerImage(1, clue.ImageUrl!, clue.SourceUrl, label, Caption: caption)] : [],
            [new AiAnswerSource(1, label, clue.SourceUrl, IsImage: hasPicture)],
            NoModel,
            ConversationId: 0,
            Depth: 0,
            IsLongThread: false,
            RetrievedSourceUrls: [clue.SourceUrl],
            CitedSourceUrls: [clue.SourceUrl]);
    }

    /// <summary>The guide's words, whole up to the limit and cut at a sentence past it.</summary>
    private static string Trim(string text, int maxCharacters)
    {
        var flat = text.Trim();
        if (flat.Length <= maxCharacters)
        {
            return flat;
        }

        var cut = flat.LastIndexOfAny(['.', '!', '?'], maxCharacters - 1);
        if (cut >= maxCharacters / 2)
        {
            return flat[..(cut + 1)];
        }

        var word = flat.LastIndexOf(' ', maxCharacters - 1);
        return $"{flat[..(word > maxCharacters / 2 ? word : maxCharacters - 1)].TrimEnd()}…";
    }
}

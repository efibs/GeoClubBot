using System.Globalization;
using System.Text;
using Entities;
using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Conversations;

/// <param name="Marker">The <c>[N]</c> this excerpt was offered under.</param>
/// <param name="Label">What the model saw as the excerpt's heading, so the reply names it the same way.</param>
/// <param name="ImageUrl">
/// The picture that comes with the excerpt, or null for text. Set only when there is a picture that can
/// actually be attached: an image chunk whose picture was lost is offered as plain text, so the model
/// is never promised an attachment that cannot appear.
/// </param>
/// <param name="Title">The source's own title, which names an attached picture better than a section path.</param>
public sealed record OfferedExcerpt(
    int Marker,
    string SourceUrl,
    string Label,
    string? ImageUrl = null,
    string? Title = null);

/// <param name="Excerpts">Every excerpt offered, so a marker in the answer can be resolved to its source.</param>
public sealed record AiPrompt(
    IReadOnlyList<AiChatMessage> Messages,
    IReadOnlyList<OfferedExcerpt> Excerpts)
{
    /// <summary>
    /// Whether any message carries a picture — the question's attachment or one replayed from earlier
    /// in the branch. A follow-up to a screenshot has no attachment of its own but still sends the
    /// screenshot, so the model asked must be able to read it.
    /// </summary>
    public bool CarriesImages => Messages.Any(message => message.Parts.OfType<AiImagePart>().Any());
}

/// <summary>
/// Assembles the messages sent to the model. The citations that come back are read by
/// <see cref="CitationResolver"/>.
///
/// Pure so prompt shape can be tested without a provider. Retrieval happens before the call rather
/// than through tool-calling: requiring tool support would exclude most of the free models this
/// feature depends on, and the weaker ones invoke tools unreliably.
/// </summary>
public static class AiPromptBuilder
{
    /// <summary>Excerpts are numbered from one so the model's citations read naturally.</summary>
    private const int FirstMarker = 1;

    /// <summary>
    /// Each paragraph answers something beta testers reported:
    /// <list type="bullet">
    /// <item>One citation style, because every other one a model invents is one more way for a picture
    /// to go missing or a number to point nowhere.</item>
    /// <item>The picture affordance leads rather than qualifies, because models otherwise fall back on
    /// the assistant's default posture and answer "I can't display images".</item>
    /// <item>The model is told it cannot see the guide pictures — it gets their text only — because
    /// "describe what it shows" had it inventing what was in them.</item>
    /// <item>A guide's region is not the user's: a cactus found in a California guide turned a question
    /// about the cactus into an answer about California.</item>
    /// <item>The guides are a floor, not a ceiling: answers stopped at what one guide said ("Khasi pines
    /// are also found outside India") or declined a list the guides only partly cover.</item>
    /// </list>
    /// </summary>
    public const string SystemPrompt = """
        You are a GeoGuessr assistant for a club Discord server. You help players identify countries
        and regions from visual clues: road markings, bollards, utility poles, licence plates,
        architecture, vegetation, scripts and the Google car itself.

        Each question comes with numbered excerpts from community guides. Cite every claim you take
        from one by writing its number in square brackets straight after the claim, like [2], or
        [2][5] for several. This is not optional: an uncited claim gives the reader no way to check
        it. Each number becomes a named, clickable link under your answer, so never write a URL,
        never add a list of sources yourself, and never use any other citation style. Never invent a
        number.

        You can show pictures, and you should. Excerpts marked (picture) come with one, and citing
        that excerpt's number attaches the picture to your reply. This is a visual game and a picture
        often settles a question faster than words, so cite pictures whenever they show what you are
        talking about. Never say you are unable to display images, and never claim to show one
        without citing it. You cannot see those pictures yourself — only the text that goes with
        them — so say what a picture is for, but never describe a detail its text does not mention.

        When the user attaches a screenshot you can see it. Describe only what is actually visible in
        it, and say so when a clue cannot be made out rather than guessing.

        A guide's country or region is where its excerpt applies, not necessarily what the user is
        asking about. Don't narrow your answer to that place unless the question does.

        Answer the whole question. When the guides cover only part of it, complete the answer from
        your own knowledge and say which part is not from the guides. When they don't cover it at
        all, say so in one short sentence, then answer. Call them "the guides", never "excerpts".

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
                // An earlier answer's numbers pointed at that turn's excerpts. This turn's are numbered
                // from one again, so left in they would read as citations of excerpts they never meant.
                ? AiChatMessage.Assistant(CitationResolver.StripMarkers(turn.Content))
                // Several people can share one branch, so each question is attributed; otherwise the
                // model reads a group discussion as one person contradicting themselves.
                : AiChatMessage.User(FormatUserTurn(turn), turn.ImageUrls));
        }

        var (excerpts, offered) = FormatExcerpts(hits);
        var pictures = offered.Where(excerpt => excerpt.ImageUrl is not null).ToList();

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
        if (pictures.Count > 0)
        {
            prompt.Append("Pictures you can attach: ")
                .AppendLine(string.Join(", ", pictures.Select(picture =>
                    $"[{picture.Marker.ToString(CultureInfo.InvariantCulture)}]")))
                .AppendLine("Cite them like any other excerpt and they are attached to your reply.")
                .AppendLine();
        }

        prompt.Append("Question: ").Append(question);

        messages.Add(AiChatMessage.User(prompt.ToString(), attachmentImageUrls));

        return new AiPrompt(messages, offered);
    }

    private static string FormatUserTurn(ConversationTurnView turn) =>
        $"<@{turn.AuthorDiscordUserId.ToString(CultureInfo.InvariantCulture)}>: {turn.Content}";

    /// <summary>
    /// Renders retrieved chunks as numbered excerpts. Every excerpt is cited the same way; one that
    /// comes with a picture says so, because citing it is how the picture reaches the user — and for a
    /// visual game the picture is often the actual answer.
    /// </summary>
    private static (string Excerpts, IReadOnlyList<OfferedExcerpt> Offered) FormatExcerpts(
        IReadOnlyList<KnowledgeHit> hits)
    {
        var builder = new StringBuilder();
        var offered = new List<OfferedExcerpt>();

        for (var index = 0; index < hits.Count; index++)
        {
            var hit = hits[index];
            var marker = FirstMarker + index;

            // Only a picture that can actually be attached is announced as one.
            var imageUrl = hit.Kind == KnowledgeChunkKind.Image && !string.IsNullOrWhiteSpace(hit.ImageUrl)
                ? hit.ImageUrl
                : null;

            builder.Append('[').Append(marker.ToString(CultureInfo.InvariantCulture)).Append("] ");
            if (imageUrl is not null)
            {
                builder.Append("(picture) ");
            }

            // The same heading is kept for the reply's source list, so a reader following [1] back
            // finds it named the way the model was shown it.
            var heading = BuildHeading(hit);

            builder.AppendLine(heading);
            builder.AppendLine(hit.Text);
            builder.Append("(source: ").Append(hit.SourceUrl).AppendLine(")").AppendLine();

            offered.Add(new OfferedExcerpt(marker, hit.SourceUrl, heading, imageUrl, hit.Title));
        }

        return (builder.ToString().TrimEnd(), offered);
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
}

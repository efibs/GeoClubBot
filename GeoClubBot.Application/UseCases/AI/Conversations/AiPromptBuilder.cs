using System.Globalization;
using System.Text;
using Entities;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Ingestion;

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
    string? Title = null)
{
    /// <summary>
    /// More pictures that come with the excerpt, beyond <see cref="ImageUrl"/>: those of the copies of
    /// its text the search collapsed into it (<see cref="KnowledgeHit.ExtraImageUrls"/>).
    /// </summary>
    public IReadOnlyList<string> ExtraImageUrls { get; init; } = [];

    /// <summary>
    /// The index chunks behind the excerpt, its merged copies included, so what an answer was offered
    /// and cited can be stored and read back. A tool's result that never was in the index (a species
    /// range) has an id too, which a read back simply does not find.
    /// </summary>
    public IReadOnlyList<Guid> ChunkIds { get; init; } = [];

    /// <summary>Every picture citing the excerpt can attach, its own first.</summary>
    public IReadOnlyList<string> ImageUrls => ImageUrl is null ? [] : [ImageUrl, .. ExtraImageUrls];
}

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
/// Pure so prompt shape can be tested without a provider. Retrieval happens before the first call,
/// whether or not the model may call tools afterwards: a model without tools still has to be able to
/// answer, and the excerpts often settle a question without any tool call — which then costs no more
/// than it ever did. The tools' results are formatted here too (<see cref="FormatExcerpt"/>), so an
/// excerpt reads the same however it was found.
/// </summary>
public static class AiPromptBuilder
{
    /// <summary>Excerpts are numbered from one so the model's citations read naturally.</summary>
    private const int FirstMarker = 1;

    /// <summary>What a PlonkIt picture is marked with: about 70 % of them carry a small distribution map.</summary>
    public const string DistributionMapNote = "usually with a distribution map";

    /// <summary>
    /// Each line answers something users reported, most of it in the rated answers of the 2026-10-23
    /// feedback export. Kept to one short, concrete instruction per lesson, because the free models
    /// that read it follow a short list and lose a long one.
    /// <list type="bullet">
    /// <item>One citation style, because every other one a model invents is one more way for a picture
    /// to go missing or a number to point nowhere.</item>
    /// <item>The picture affordance leads rather than qualifies, because models otherwise fall back on
    /// the assistant's default posture and answer "I can't display images" — and the rule that only a
    /// cited picture is attached, because "shown in the attached pictures" was written of pictures that
    /// were not. Distribution questions want the map: "without the image … that shows the exact
    /// distribution, this answer is basically useless", while the cited PlonkIt tip carried it.</item>
    /// <item>The model is told it cannot see the guide pictures — it gets their text only — because
    /// "describe what it shows" had it inventing what was in them.</item>
    /// <item>The direct answer first: "a clear answer 'They are by far most common on the mainland but
    /// can be found elsewhere too' would have been great".</item>
    /// <item>Qualifiers kept: "99% Peninsula" became "mainly", and "notably rare in the Northeast"
    /// became nine named states, of which users then found counterexamples.</item>
    /// <item>A related thing is not the asked thing: "I asked about snake traps. Not possum guards", "I
    /// meant snow bollard, not regular bollard". (A snow bollard is what the guides call a snow pole —
    /// the search tool is told so — and the answer's mistake was the road bollards it described.)</item>
    /// <item>One country stays one country, and a screenshot is judged against it: "I don't care about
    /// other countries if I ask about regionguessing Australia", and the bollard "wasn't Victoria".
    /// A guide's region is still not the user's: a cactus found in a California guide had turned a
    /// question about the cactus into an answer about California.</item>
    /// <item>Follow-ups keep their thread: "How can I clearly identify Sabah?" after a Malaysia question
    /// was answered with how to recognise Malaysia.</item>
    /// <item>Coverage, not the street: "the license plate thingy is basically useless in GeoGuessr as
    /// like 99.9% of the license plates are fully blurred".</item>
    /// <item>The guides are a floor, not a ceiling: answers stopped at what one guide said ("Khasi pines
    /// are also found outside India") or declined a list the guides only partly cover.</item>
    /// </list>
    /// </summary>
    public const string SystemPrompt = """
        You are a GeoGuessr assistant for a club Discord server. Players ask how to tell countries and
        regions apart by what Street View coverage shows.

        Each question comes with numbered excerpts from community guides. Cite every claim you take
        from one by writing its number in square brackets straight after the claim, like [2], or
        [2][5] for several; an uncited claim cannot be checked. Each number becomes a named, clickable
        link under your answer, so never write a URL, never add a list of sources yourself, and never
        use any other citation style. Never invent a number.

        You can show pictures, and you should. Excerpts marked (picture) or (N pictures) come with
        them, and citing the excerpt's number attaches its pictures to your reply. For "where", "which
        region" or distribution questions, cite a picture that shows the distribution; those marked
        "usually with a distribution map" mostly do. When the user asks to see something, cite a
        picture of it. Never say you are unable to display images, and never say a picture is attached
        unless you cite it. You cannot see those pictures yourself, only their text, so never describe
        a detail the text does not mention.

        How to answer:
        - Lead with the direct answer to the exact question, then the specifics that back it. No
          generic filler.
        - Keep the guides' qualifiers: "mainly", "99%" or "rare in" never become "only" or "never".
          Never turn a vague note into a complete list ("rare in the Northeast" does not name nine
          states), and say what you infer.
        - When the guides describe something related but different from what was asked (snake traps
          asked, possum guards described; snow bollards asked, ordinary road bollards described), say
          so plainly instead of treating them as the same thing.
        - A question about one country stays in that country, with the clues that tell its regions
          apart. Mention another country only briefly, as a look-alike to rule out. A guide's country
          is where its excerpt applies, not necessarily what the user is asking about.
        - A follow-up continues the earlier turns' topic and country unless it says otherwise ("How can
          I identify Sabah?" after a Malaysia question means within Malaysia).
        - When the user attaches a screenshot you can see it. Describe only what is visible, and say so
          when a clue cannot be made out. When they name a country, judge the screenshot against that
          country's clues, and say when it does not match a guide's description.
        - Prefer clues visible in coverage: poles, bollards, road lines, signs, vegetation,
          architecture, the Google car and its generation. Licence plates and faces are blurred in
          Street View, so plate letters are rarely usable.
        - Answer the whole question. When the guides cover only part of it, complete the answer from
          your own knowledge and say which part is not from the guides. When they don't cover it at
          all, say so in one short sentence, then answer. Call them "the guides", never "excerpts".

        Keep answers short and concrete.
        """;

    /// <summary>
    /// Builds the full message list: system prompt, replayed history, then the current question with
    /// its retrieved excerpts.
    /// </summary>
    /// <param name="carried">
    /// How many of <paramref name="hits"/>, from the first, are the excerpts the previous answer cited,
    /// offered again in the order it cited them. The model is told, because "that picture" or "source
    /// 3" in a follow-up means one of them, and nothing else in the prompt says which.
    /// </param>
    public static AiPrompt Build(
        ConversationContext context,
        string question,
        IReadOnlyList<string> attachmentImageUrls,
        IReadOnlyList<KnowledgeHit> hits,
        int carried = 0)
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
            prompt.AppendLine("Guide excerpts:");
            if (DescribeCarried(Math.Min(carried, hits.Count), hits.Count) is { } note)
            {
                prompt.AppendLine(note);
            }

            prompt.AppendLine(excerpts).AppendLine();
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

    /// <summary>
    /// The same prompt with <paramref name="paragraph"/> added to its system prompt — the tools'
    /// instructions, which only belong there when tools are offered. Added to the one system prompt
    /// rather than sent as a message of its own, so a model that weighs only the first system message
    /// still reads them.
    /// </summary>
    public static AiPrompt AppendToSystemPrompt(AiPrompt prompt, string paragraph)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        if (string.IsNullOrWhiteSpace(paragraph) || prompt.Messages.Count == 0 || prompt.Messages[0].Role != AiChatRole.System)
        {
            return prompt;
        }

        var system = AiChatMessage.System($"{prompt.Messages[0].ToPlainText()}\n\n{paragraph}");
        return prompt with { Messages = [system, .. prompt.Messages.Skip(1)] };
    }

    /// <summary>
    /// One retrieved chunk as the model is shown it, numbered <paramref name="marker"/>: the heading,
    /// "(picture)" or "(N pictures)" when pictures can be attached by citing it, the text, and the
    /// source. Every excerpt is cited the same way; one that comes with pictures says so, because citing
    /// it is how they reach the user — and for a visual game the picture is often the actual answer.
    ///
    /// A PlonkIt picture is marked as usually carrying a distribution map. About 70 % of them do — an
    /// inset drawn into the tip's picture, measured on 973 pictures from 15 guides — yet only 4 of 642
    /// such tips say "map" in their text. Shown only "(picture)", the model told a user asking for the
    /// distribution "I don't have the actual distribution image" while it cited one.
    /// </summary>
    /// <param name="offered">What the excerpt is offered as, to resolve a citation of it.</param>
    public static string FormatExcerpt(KnowledgeHit hit, int marker, out OfferedExcerpt offered)
    {
        ArgumentNullException.ThrowIfNull(hit);

        // Only a picture that can actually be attached is announced as one.
        var imageUrl = hit.Kind == KnowledgeChunkKind.Image && !string.IsNullOrWhiteSpace(hit.ImageUrl)
            ? hit.ImageUrl
            : null;

        IReadOnlyList<string> extras = imageUrl is null
            ? []
            : [.. hit.ExtraImageUrls
                .Where(url => !string.IsNullOrWhiteSpace(url) && !string.Equals(url, imageUrl, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)];

        // The same heading is kept for the reply's source list, so a reader following [1] back
        // finds it named the way the model was shown it.
        var heading = BuildHeading(hit);

        var builder = new StringBuilder();
        builder.Append('[').Append(marker.ToString(CultureInfo.InvariantCulture)).Append("] ");
        if (imageUrl is not null)
        {
            builder.Append('(')
                .Append(extras.Count == 0
                    ? "picture"
                    : $"{(extras.Count + 1).ToString(CultureInfo.InvariantCulture)} pictures");

            if (IsPlonkIt(hit))
            {
                builder.Append(", ").Append(DistributionMapNote);
            }

            builder.Append(") ");
        }

        builder.AppendLine(heading);
        builder.AppendLine(hit.Text);
        builder.Append("(source: ").Append(hit.SourceUrl).Append(')');

        offered = new OfferedExcerpt(marker, hit.SourceUrl, heading, imageUrl, hit.Title)
        {
            ExtraImageUrls = extras,
            ChunkIds = [.. hit.ChunkIds]
        };
        return builder.ToString();
    }

    /// <summary>
    /// Whether a chunk is a PlonkIt tip: by its source type, which every indexed chunk carries, or by
    /// its link for a hit that came without one.
    /// </summary>
    private static bool IsPlonkIt(KnowledgeHit hit) =>
        string.Equals(hit.SourceType, SourceLinkClassifier.PlonkIt, StringComparison.OrdinalIgnoreCase)
        || (Uri.TryCreate(hit.SourceUrl, UriKind.Absolute, out var url)
            && (url.Host.Equals("plonkit.net", StringComparison.OrdinalIgnoreCase)
                || url.Host.EndsWith(".plonkit.net", StringComparison.OrdinalIgnoreCase)));

    /// <summary>What the model is told of the excerpts carried from the previous answer, or null for none.</summary>
    private static string? DescribeCarried(int carried, int total)
    {
        if (carried <= 0)
        {
            return null;
        }

        var which = carried == 1
            ? "[1] is the excerpt your previous answer cited"
            : $"[1] to [{carried.ToString(CultureInfo.InvariantCulture)}] are the excerpts your previous answer cited, in the order it cited them";

        return carried < total ? $"({which}; the rest were found for this question.)" : $"({which}.)";
    }

    private static string FormatUserTurn(ConversationTurnView turn) =>
        $"<@{turn.AuthorDiscordUserId.ToString(CultureInfo.InvariantCulture)}>: {turn.Content}";

    /// <summary>Renders retrieved chunks as numbered excerpts, one <see cref="FormatExcerpt"/> each.</summary>
    private static (string Excerpts, IReadOnlyList<OfferedExcerpt> Offered) FormatExcerpts(
        IReadOnlyList<KnowledgeHit> hits)
    {
        var builder = new StringBuilder();
        var offered = new List<OfferedExcerpt>();

        for (var index = 0; index < hits.Count; index++)
        {
            builder.AppendLine(FormatExcerpt(hits[index], FirstMarker + index, out var excerpt)).AppendLine();
            offered.Add(excerpt);
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

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Entities;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;

namespace UseCases.UseCases.AI.Retrieval;

/// <summary>What the query planner made of a question, ready to search with.</summary>
/// <param name="StandaloneQuestion">
/// The question rewritten to stand on its own: "What about corn?" becomes "Where in the Philippines is
/// corn grown?". Null when the reply had none.
/// </param>
/// <param name="SearchPhrasings">Short searches worded the way the guides word things: "snow poles", not "snow bollards".</param>
/// <param name="Countries">The countries the question is about, as the model named them.</param>
/// <param name="KeyPhrases">Literal phrases a sentence answering it would contain, e.g. "no supports".</param>
public sealed record RetrievalPlan(
    string? StandaloneQuestion,
    IReadOnlyList<string> SearchPhrasings,
    IReadOnlyList<string> Countries,
    IReadOnlyList<string> KeyPhrases)
{
    public bool IsEmpty =>
        StandaloneQuestion is null && SearchPhrasings.Count == 0 && Countries.Count == 0 && KeyPhrases.Count == 0;
}

/// <summary>
/// The prompt that asks a model to turn a question and its conversation into searches, and the
/// reader of its reply.
///
/// Measured on the rated answers of 2026-10-23: a follow-up searched by its own words found the guide
/// that answers it in one of five cases; rewritten to stand on its own, in all five. A single-turn
/// question gains too when it uses other words than the guides — "snow bollards" put the Finnish snow
/// poles 43rd, "snow poles" 4th.
///
/// Pure: the caller makes the request. The reply is read defensively, because free models wrap JSON in
/// prose, code fences and reasoning, and a reply that cannot be read only means the question is
/// searched without a plan.
/// </summary>
public static partial class RetrievalQueryPlanner
{
    /// <summary>
    /// Kept low: the planner rewrites rather than writes, and the same question should be searched the
    /// same way twice.
    /// </summary>
    public const double Temperature = 0.1;

    /// <summary>Most countries kept from a reply; more reads as a question about everywhere.</summary>
    public const int MaxCountries = 5;

    /// <summary>Most key phrases kept from a reply.</summary>
    public const int MaxKeyPhrases = 3;

    /// <summary>Earlier messages shown to the planner. It needs the thread's subject, not its history.</summary>
    private const int MaxConversationTurns = 6;

    /// <summary>Characters kept of each earlier message; enough to say what it was about.</summary>
    private const int MaxTurnCharacters = 400;

    /// <summary>Longest wording kept from a reply; anything longer is the model answering instead.</summary>
    private const int MaxWordingCharacters = 300;

    /// <summary>
    /// The instruction. The examples of the guides' own terms are the ones the feedback showed missed:
    /// "snow bollards" against the guides' "snow poles", "electricity poles" against "utility poles".
    /// Two rules answer what the first measured replies got wrong: "Gen 4" was searched as a mobile
    /// network ("4G antenna locations Brazil"), and "where can you find tree yucca?" was given the
    /// countries the model guessed the answer lay in — which a country boost then held to them.
    /// </summary>
    public const string SystemPrompt = """
        You prepare searches for a GeoGuessr assistant. Its library is community guides to telling
        countries and regions apart: road lines, bollards, utility poles, licence plates, signs,
        scripts, vegetation, architecture, landscape, and the Google car and its camera generations
        (Gen 2, Gen 3, Gen 4 — never mobile networks).

        Read the conversation and the latest question, and reply with one JSON object and nothing else:
        {"question": "...", "searches": ["..."], "countries": ["..."], "phrases": ["..."]}

        Write everything in English, the language of the guides.
        question: the latest question rewritten to stand on its own. Replace words like "this", "it",
        "there" and "what about" with what they refer to in the conversation. Keep its meaning and
        its country; never answer it.
        searches: up to 3 short searches (3 to 8 words each) worded the way a guide would describe the
        answer, naming the country or region when there is one, and using the guides' usual terms:
        "snow poles" (not snow bollards or snow markers), "utility poles" (not electricity or power
        poles), "Google car", "road lines", "licence plates".
        countries: the countries the question or the conversation names, and the country of any region
        or city it names (Sabah: Malaysia, Kyushu: Japan, Tripura: India). Never a country you only
        expect the answer to lie in. Use [] when it asks which countries have something, or names no
        place.
        phrases: up to 3 distinctive phrases (1 to 3 words) that a guide sentence answering the
        question would contain word for word, such as "no supports" or "single insulator". Never a
        place name or a common word like colour, shape, height or difference. Use [] when unsure.
        """;

    /// <summary>The request messages: the instruction, then the conversation and the question as one user message.</summary>
    /// <param name="turns">The branch the question replies to, oldest first; empty for a fresh question.</param>
    /// <param name="hasScreenshot">The question carries a picture the planner cannot see, and should not guess at.</param>
    public static IReadOnlyList<AiChatMessage> BuildMessages(
        IReadOnlyList<ConversationTurnView> turns,
        string question,
        bool hasScreenshot)
    {
        ArgumentNullException.ThrowIfNull(turns);

        var prompt = new StringBuilder();
        var recent = turns.Where(turn => !string.IsNullOrWhiteSpace(turn.Content)).TakeLast(MaxConversationTurns).ToList();

        if (recent.Count > 0)
        {
            prompt.AppendLine("Conversation so far:");
            foreach (var turn in recent)
            {
                // An earlier answer's citations point at excerpts the planner never sees.
                var content = turn.Role == AiTurnRole.Assistant
                    ? CitationResolver.StripMarkers(turn.Content)
                    : turn.Content;

                prompt.Append(turn.Role == AiTurnRole.Assistant ? "Assistant: " : "User: ")
                    .AppendLine(Shorten(content, MaxTurnCharacters));
            }

            prompt.AppendLine();
        }

        prompt.Append("Latest question: ").AppendLine(Shorten(question, MaxTurnCharacters * 2));

        if (hasScreenshot)
        {
            prompt.AppendLine("(The user attached a screenshot, which you cannot see.)");
        }

        return [AiChatMessage.System(SystemPrompt), AiChatMessage.User(prompt.ToString().TrimEnd())];
    }

    /// <summary>
    /// Reads the planner's reply, or returns null when there is nothing usable in it. Tolerates what
    /// free models do to JSON: reasoning before it, a code fence around it, prose after it, trailing
    /// commas, a single string where a list was asked for, and other names for the same field.
    /// </summary>
    public static RetrievalPlan? Parse(string? reply, int maxPhrasings)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return null;
        }

        using var document = FindObject(Reasoning().Replace(reply, string.Empty));
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        var plan = new RetrievalPlan(
            ReadStrings(root, "question", "standalone_question", "standaloneQuestion", "rewritten").FirstOrDefault(),
            [.. ReadStrings(root, "searches", "search", "queries", "phrasings").Take(Math.Max(0, maxPhrasings))],
            [.. ReadStrings(root, "countries", "country").Take(MaxCountries)],
            // A "phrase" of a whole sentence would match nothing literally.
            [.. ReadStrings(root, "phrases", "keywords", "key_phrases", "keyPhrases")
                .Where(phrase => phrase.Split(' ').Length <= 4)
                .Take(MaxKeyPhrases)]);

        return plan.IsEmpty ? null : plan;
    }

    /// <summary>
    /// The JSON object in a reply. Each opening brace is tried in turn, because prose before the object
    /// may hold braces of its own; each is read to its matching closing brace, so prose after the object
    /// may too, and then to the last closing brace, for an object whose strings confuse the matching.
    /// </summary>
    private static JsonDocument? FindObject(string text)
    {
        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        var last = text.LastIndexOf('}');

        for (var start = text.IndexOf('{', StringComparison.Ordinal);
             start >= 0 && start < last;
             start = text.IndexOf('{', start + 1))
        {
            foreach (var end in new[] { MatchingBrace(text, start), last }.Where(end => end > start).Distinct())
            {
                try
                {
                    var document = JsonDocument.Parse(text[start..(end + 1)], options);
                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return document;
                    }

                    document.Dispose();
                }
                catch (JsonException)
                {
                    // Not the object yet; try the next candidate.
                }
            }
        }

        return null;
    }

    /// <summary>The brace closing the one at <paramref name="start"/>, braces inside strings skipped; -1 when none does.</summary>
    private static int MatchingBrace(string text, int start)
    {
        var depth = 0;
        var inString = false;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (character == '\\')
                {
                    index++;
                }
                else if (character == '"')
                {
                    inString = false;
                }
            }
            else if (character == '"')
            {
                inString = true;
            }
            else if (character == '{')
            {
                depth++;
            }
            else if (character == '}' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The first of <paramref name="names"/> present, as cleaned, distinct strings — a list, or a single
    /// string where a list was asked for.
    /// </summary>
    private static IEnumerable<string> ReadStrings(JsonElement root, params string[] names)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            IEnumerable<JsonElement> values = property.Value.ValueKind switch
            {
                JsonValueKind.Array => property.Value.EnumerateArray(),
                JsonValueKind.String => [property.Value],
                _ => []
            };

            return values
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => Clean(value.GetString()))
                .Where(value => value.Length > 0 && value.Length <= MaxWordingCharacters)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return [];
    }

    private static string Clean(string? value) =>
        value is null ? string.Empty : string.Join(' ', value.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

    private static string Shorten(string text, int length)
    {
        var collapsed = Clean(text);
        return collapsed.Length <= length ? collapsed : collapsed[..(length - 1)] + "…";
    }

    /// <summary>A reasoning model's thinking, when it is returned inline with the answer.</summary>
    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Reasoning();
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// A model's comparison of a new answer with the rated one. Indicative only: one free model's reading,
/// which cannot see the pictures, may be the model that wrote the new answer, and is not the member who
/// rated the old one.
/// </summary>
/// <param name="Verdict">"better", "same" or "worse" — the new answer against the old; null when the reply could not be read.</param>
/// <param name="AddressesComment">Whether the new answer fixes what the member's comment asked for; null without a comment.</param>
/// <param name="HallucinationRisk">"low", "medium" or "high": how much of the new answer the excerpts do not back.</param>
/// <param name="Raw">The judge's reply as it came, when it could not be read.</param>
public sealed record JudgeVerdict(
    string? Verdict,
    bool? AddressesComment,
    string? HallucinationRisk,
    string Reasons,
    string? Model,
    double Seconds,
    string? Raw = null)
{
    public bool IsReadable => Verdict is not null;
}

/// <summary>
/// Asks a model whether the answer <c>answer</c> got now is better than the one the member rated, given
/// the question, the member's comment and the excerpts the new answer was written from. The prompt and
/// the reading of its reply are pure, so both are tested without a model.
/// </summary>
public static class AnswerJudge
{
    /// <summary>Excerpts beyond this are cut: a tool-heavy answer can carry thirty, and the judge needs the gist.</summary>
    public const int MaxExcerptCharacters = 24_000;

    /// <summary>The judge reads a long prompt; a model that cannot hold it is not asked.</summary>
    public const int MinContextLength = 32_768;

    public const double Temperature = 0;

    private const string Fence = "<<<";
    private const string FenceEnd = ">>>";

    public const string SystemPrompt = """
        You compare two answers that a GeoGuessr club's Discord assistant gave to the same question: the
        OLD answer, which a club member rated, and a NEW answer from a changed version of the assistant.
        Judge the NEW answer against the OLD one, for the member who asked.

        Judge by, in this order:
        1. Support: claims should be backed by the guide excerpts the NEW answer was given. A specific
           claim (a region, a list of states, a percentage, a clue) that the excerpts do not support is a
           hallucination risk, however plausible it sounds. General knowledge said to be such is fine.
        2. Whether it answers the exact question, in the country or region asked about, directly and
           specifically. A follow-up continues the conversation's topic.
        3. Whether it fixes what the member's comment on the OLD answer complains about, when there is one.
        4. Usefulness in GeoGuessr: concrete clues visible in Street View coverage beat generic facts
           (licence plates are almost always blurred).

        You cannot see pictures. An answer that attaches a picture the member wanted (a distribution map,
        an example) is better than one that does not, other things equal; the attached pictures are listed.
        The OLD answer's [n] numbers pointed at excerpts you are not shown; only its cited sources are
        listed, so do not hold its citations against it. Length is not quality.

        Reply with one JSON object and nothing else:
        {"verdict": "better" | "same" | "worse", "addressesComment": true | false | null, "hallucinationRisk": "low" | "medium" | "high", "reasons": "two or three sentences"}
        - verdict: is the NEW answer better than, about the same as, or worse than the OLD one?
        - addressesComment: does the NEW answer fix what the comment asks for? null when there is no comment.
        - hallucinationRisk: how much of the NEW answer states specifics its excerpts do not support.
        """;

    /// <summary>What the judge is asked about one record.</summary>
    /// <param name="newAnswerAsPosted">The new answer as Discord shows it: the message, its source list beneath.</param>
    /// <param name="pictures">The pictures attached to the new answer, one line each.</param>
    public static IReadOnlyList<AiChatMessage> BuildMessages(
        RatedQuestion rated,
        AnswerRun run,
        string newAnswerAsPosted,
        IReadOnlyList<string> pictures)
    {
        ArgumentNullException.ThrowIfNull(rated);
        ArgumentNullException.ThrowIfNull(run);

        var record = rated.Record;
        var old = record.Transcript.OrderBy(turn => turn.Ordinal).LastOrDefault(turn =>
            string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase));

        var prompt = new StringBuilder();
        if (rated.Context.Count == 0)
        {
            prompt.AppendLine("Conversation before the question: none, a fresh question.");
        }
        else
        {
            prompt.AppendLine("Conversation before the question, oldest first:");
            foreach (var turn in rated.Context)
            {
                prompt.Append(turn.Role == Entities.AiTurnRole.Assistant ? "Assistant: " : "User: ").AppendLine(turn.Content.Trim());
            }
        }

        prompt.AppendLine().Append("Question: ").AppendLine(rated.Question.Trim());
        if (rated.QuestionHadScreenshot || run.Notes.Count > 0)
        {
            prompt.AppendLine(
                "Note: the member attached a screenshot. The OLD answer could see it; the NEW one could not (its link had expired). "
                + "Do not count that against the NEW answer, and say so if it decides the comparison.");
        }

        prompt.AppendLine()
            .Append("The member rated the OLD answer: ").AppendLine(rated.IsNegative ? "thumbs down" : "thumbs up")
            .Append("The member's comment: ")
            .AppendLine(string.IsNullOrWhiteSpace(record.Comment) ? "(none)" : $"\"{record.Comment.Trim()}\"");

        prompt.AppendLine()
            .Append("OLD answer (").Append(old?.ModelId ?? record.ModelId ?? "model unknown").AppendLine("):")
            .AppendLine(Fence).AppendLine(old?.Content.Trim() ?? "(not in the export)").AppendLine(FenceEnd)
            .Append("OLD answer's cited sources: ")
            .AppendLine(record.CitedSourceUrls.Count == 0 ? "none" : string.Join(", ", record.CitedSourceUrls));

        // The model's own text where it was kept: its [n] are the numbers of the excerpts below, which the
        // posted text has renumbered in citation order.
        prompt.AppendLine();
        if (run.ModelText is { Length: > 0 } written)
        {
            prompt.Append("NEW answer (").Append(run.Answer?.ModelUsed ?? "no model")
                .AppendLine("), as the model wrote it; its [n] are the numbers of the excerpts below:")
                .AppendLine(Fence).AppendLine(written.Trim()).AppendLine(FenceEnd);
        }
        else
        {
            prompt.Append("NEW answer (").Append(run.Answer?.ModelUsed ?? "no model")
                .AppendLine("), as posted; its [n] are the sources listed beneath it, each excerpt below names its source:")
                .AppendLine(Fence).AppendLine(newAnswerAsPosted.Trim()).AppendLine(FenceEnd);
        }

        prompt
            .Append("Pictures attached to the NEW answer: ")
            .AppendLine(pictures.Count == 0 ? "none" : string.Empty);
        foreach (var picture in pictures)
        {
            prompt.Append("- ").AppendLine(picture);
        }

        var excerpts = run.ExcerptsShown.Length <= MaxExcerptCharacters
            ? run.ExcerptsShown
            : $"{run.ExcerptsShown[..MaxExcerptCharacters]}\n… (the rest cut for length)";

        prompt.AppendLine()
            .AppendLine("Guide excerpts the NEW answer was given (those in its prompt, then what its tool calls returned):")
            .AppendLine(Fence).AppendLine(excerpts.Length == 0 ? "(none)" : excerpts).AppendLine(FenceEnd);

        return [AiChatMessage.System(SystemPrompt), AiChatMessage.User(prompt.ToString())];
    }

    /// <summary>
    /// The verdict in a judge's reply, or one with a null <see cref="JudgeVerdict.Verdict"/> and the raw
    /// reply when none can be read. Models wrap JSON in fences or think aloud before it, so the first
    /// object that reads as a verdict is taken, wherever it starts.
    /// </summary>
    public static JudgeVerdict Parse(string reply, string? model, double seconds)
    {
        foreach (var start in Starts(reply))
        {
            var end = reply.LastIndexOf('}');
            while (end > start)
            {
                if (TryRead(reply[start..(end + 1)], model, seconds) is { } verdict)
                {
                    return verdict;
                }

                end = reply.LastIndexOf('}', end - 1);
            }
        }

        return new JudgeVerdict(null, null, null, "The judge's reply could not be read.", model, seconds, reply);
    }

    /// <summary>Asks the judge, or returns the verdict cached for exactly this prompt and chain.</summary>
    public static async Task<JudgeVerdict?> JudgeAsync(
        IChatModelClient client,
        IReadOnlyList<string> chain,
        IReadOnlyList<AiChatMessage> messages,
        AnswerCache cache,
        string answerKey,
        CancellationToken cancellationToken)
    {
        var key = $"{answerKey}-{KeyFor(chain, messages)}";
        if (cache.ReadVerdict(key) is { } cached)
        {
            return cached;
        }

        var started = Stopwatch.GetTimestamp();
        var completion = await client
            .CompleteAsync(new AiChatRequest(chain, messages, Temperature), cancellationToken)
            .ConfigureAwait(false);
        var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;

        if (completion.IsFailure)
        {
            Console.Error.WriteLine($"Judge: failed ({completion.Error.Code}) after {seconds:0.0} s.");
            return null;
        }

        var verdict = Parse(completion.Value.Text, completion.Value.ModelUsed, seconds);

        // An unreadable reply is shown but not kept, so a rerun asks again.
        if (verdict.IsReadable)
        {
            cache.WriteVerdict(key, verdict);
        }

        return verdict;
    }

    private static string KeyFor(IReadOnlyList<string> chain, IReadOnlyList<AiChatMessage> messages) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join(",", chain) + "\u001D" + string.Join("\u001E", messages.Select(message => message.ToPlainText())))))[..12]
            .ToLowerInvariant();

    private static IEnumerable<int> Starts(string reply)
    {
        for (var index = reply.IndexOf('{'); index >= 0; index = reply.IndexOf('{', index + 1))
        {
            yield return index;
        }
    }

    private static JudgeVerdict? TryRead(string json, string? model, double seconds)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var verdict = Normalise(Property(root, "verdict"), ("better", "better"), ("improved", "better"), ("same", "same"),
                ("equal", "same"), ("similar", "same"), ("worse", "worse"));
            if (verdict is null)
            {
                return null;
            }

            var risk = Normalise(Property(root, "hallucinationRisk") ?? Property(root, "hallucination_risk"),
                ("low", "low"), ("medium", "medium"), ("moderate", "medium"), ("high", "high"));

            return new JudgeVerdict(
                verdict,
                ReadBool(root, "addressesComment") ?? ReadBool(root, "addresses_comment"),
                risk,
                ReadReasons(root),
                model,
                seconds);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Property(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
            }
        }

        return null;
    }

    private static string? Normalise(string? value, params (string Word, string Meaning)[] words)
    {
        var text = value?.Trim().Trim('"').ToLowerInvariant();
        return text is null ? null : words.FirstOrDefault(word => text.StartsWith(word.Word, StringComparison.Ordinal)).Meaning;
    }

    private static bool? ReadBool(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => property.Value.GetString()?.Trim().ToLowerInvariant() switch
                {
                    "true" or "yes" => true,
                    "false" or "no" => false,
                    _ => null
                },
                _ => null
            };
        }

        return null;
    }

    private static string ReadReasons(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "reasons", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(property.Name, "reason", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join(" ", property.Value.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())),
                _ => property.Value.GetRawText()
            };
        }

        return string.Empty;
    }
}

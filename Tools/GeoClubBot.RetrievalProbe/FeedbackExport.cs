using System.Text.Json;
using Entities;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Feedback;

namespace GeoClubBot.RetrievalProbe;

/// <summary>A rated answer, and the question it answered.</summary>
/// <param name="Question">
/// The message the rated answer responded to. Not the conversation's opening message: a follow-up is
/// retrieved for by its own words together with <paramref name="Context"/>.
/// </param>
/// <param name="QuestionHadScreenshot">
/// The question carried a screenshot, whose pixels were searched as well. Discord's attachment links
/// expire within a day, so a replay searches by the text alone, and says so.
/// </param>
/// <param name="Context">
/// The branch before the question, oldest first, as the bot replays it: empty for a fresh question. A
/// follow-up is searched with it, as the bot searches it.
/// </param>
/// <param name="CarriedSourceUrls">
/// The guides the rated answer was offered again because the answer before it had cited them: the
/// first of <see cref="AiFeedbackRecord.RetrievedSourceUrls"/>, which no search found. Empty for a
/// fresh question, and for exports or answers from before excerpts were carried.
/// </param>
/// <param name="Number">The record's line in the export, from one: how reports number it, and <c>--only</c> picks it.</param>
public sealed record RatedQuestion(
    AiFeedbackRecord Record,
    string Question,
    bool QuestionHadScreenshot,
    IReadOnlyList<ConversationTurnView> Context,
    IReadOnlyList<string> CarriedSourceUrls,
    int Number = 0)
{
    public bool IsNegative => string.Equals(Record.Rating, "negative", StringComparison.OrdinalIgnoreCase);

    public bool IsFollowUp => Context.Count > 0;

    /// <summary>
    /// The guides retrieval offered the rated answer: <see cref="AiFeedbackRecord.RetrievedSourceUrls"/>
    /// without the carried ones. A carried guide the search also found was listed once, among the
    /// carried, so it cannot be told apart and is left out of the comparison either way.
    /// </summary>
    public IReadOnlyList<string> SearchedSourceUrls => [.. Record.RetrievedSourceUrls.Skip(CarriedSourceUrls.Count)];
}

/// <summary>
/// Reads an <c>/ai feedback-export</c> file into the bot's own export records, so a change to the
/// export's shape breaks the probe's build rather than its results.
/// </summary>
public static class FeedbackExport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<RatedQuestion> Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new ProbeException($"No export at '{path}'. Download one with /ai feedback-export in Discord.");
        }

        return Parse(File.ReadLines(path));
    }

    public static IReadOnlyList<RatedQuestion> Parse(IEnumerable<string> lines)
    {
        var questions = new List<RatedQuestion>();
        var lineNumber = 0;

        foreach (var line in lines)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AiFeedbackRecord record;
            try
            {
                record = JsonSerializer.Deserialize<AiFeedbackRecord>(line, Options)
                         ?? throw new ProbeException($"Line {lineNumber} of the export is empty.");
            }
            catch (JsonException ex)
            {
                throw new ProbeException($"Line {lineNumber} of the export is not a feedback record: {ex.Message}");
            }

            // The transcript ends with the rated answer; the last user turn is what it answered, and
            // everything before it is the branch the bot replayed with it.
            var transcript = record.Transcript.OrderBy(turn => turn.Ordinal).ToList();
            var asked = transcript.FindLastIndex(turn => string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase));

            if (asked >= 0 && !string.IsNullOrWhiteSpace(transcript[asked].Content))
            {
                var context = transcript.Take(asked)
                    .Select(turn => new ConversationTurnView(
                        string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? AiTurnRole.Assistant : AiTurnRole.User,
                        turn.AuthorDiscordUserId,
                        turn.Content,
                        turn.ImageUrls))
                    .ToList();

                questions.Add(new RatedQuestion(
                    record, transcript[asked].Content, transcript[asked].ImageUrls.Count > 0, context,
                    CarriedSourceUrls(record, asked > 0 ? transcript[asked - 1] : null), lineNumber));
            }
        }

        return questions;
    }

    /// <summary>
    /// The guides offered first because <paramref name="previous"/>, the answer the question replied to,
    /// cited them: the opening run of the offered guides that it cited. Only when it kept the chunks it
    /// cited, since an answer from before then had nothing carried after it.
    /// </summary>
    private static IReadOnlyList<string> CarriedSourceUrls(AiFeedbackRecord record, AiFeedbackTurnRecord? previous)
    {
        if (previous is null
            || !string.Equals(previous.Role, "assistant", StringComparison.OrdinalIgnoreCase)
            || previous.CitedChunkIds is not { Count: > 0 }
            || previous.CitedSourceUrls is not { Count: > 0 } cited)
        {
            return [];
        }

        var citedThen = cited.ToHashSet(StringComparer.Ordinal);
        return [.. record.RetrievedSourceUrls.TakeWhile(citedThen.Contains)];
    }
}

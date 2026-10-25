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
public sealed record RatedQuestion(
    AiFeedbackRecord Record,
    string Question,
    bool QuestionHadScreenshot,
    IReadOnlyList<ConversationTurnView> Context)
{
    public bool IsNegative => string.Equals(Record.Rating, "negative", StringComparison.OrdinalIgnoreCase);

    public bool IsFollowUp => Context.Count > 0;
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
                    record, transcript[asked].Content, transcript[asked].ImageUrls.Count > 0, context));
            }
        }

        return questions;
    }
}

using System.Text.Json;
using UseCases.UseCases.AI.Feedback;

namespace GeoClubBot.RetrievalProbe;

/// <summary>A rated answer, and the question it answered.</summary>
/// <param name="Question">
/// The message the rated answer responded to — the text that was embedded to retrieve for it. Not
/// the conversation's opening message: a follow-up is retrieved for by its own words.
/// </param>
/// <param name="QuestionHadScreenshot">
/// The question carried a screenshot, whose pixels were searched as well. Discord's attachment links
/// expire within a day, so a replay searches by the text alone, and says so.
/// </param>
public sealed record RatedQuestion(AiFeedbackRecord Record, string Question, bool QuestionHadScreenshot)
{
    public bool IsNegative => string.Equals(Record.Rating, "negative", StringComparison.OrdinalIgnoreCase);
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

            // The transcript ends with the rated answer; the last user turn is what it answered.
            var asked = record.Transcript
                .OrderBy(turn => turn.Ordinal)
                .LastOrDefault(turn => string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase));

            if (asked is not null && !string.IsNullOrWhiteSpace(asked.Content))
            {
                questions.Add(new RatedQuestion(record, asked.Content, asked.ImageUrls.Count > 0));
            }
        }

        return questions;
    }
}

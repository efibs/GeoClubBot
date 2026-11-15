using System.Text;
using Discord;
using UseCases.UseCases.AI.Progress;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

/// <summary>
/// Renders an answer's steps as a few lines of small text (#481): what is done ticked, what is running
/// with an hourglass. Pure, so the wording is tested as members read it.
/// </summary>
public static class AiProgressFormatter
{
    /// <summary>Lines shown at most; older steps fold into one line above them.</summary>
    public const int MaxLines = 10;

    /// <summary>A detail is the model's or the question's words, and a whole sentence of them reads as noise.</summary>
    public const int MaxDetailLength = 80;

    /// <summary>The steps as Discord text, or null when there are none to show yet.</summary>
    public static string? Render(IReadOnlyList<AiProgressStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        var shown = steps;
        if (steps.Count > MaxLines)
        {
            var folded = steps.Count - (MaxLines - 1);
            builder.Append("-# … ").Append(folded).Append(folded == 1 ? " earlier step" : " earlier steps").Append('\n');
            shown = [.. steps.Skip(folded)];
        }

        foreach (var step in shown)
        {
            builder.Append("-# ")
                .Append(step.Done ? "✓ " : "⏳ ")
                .Append(Describe(step))
                .Append(step.Done ? string.Empty : "…")
                .Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    /// <summary>What one step is doing, in the present tense, its detail quoted as written.</summary>
    public static string Describe(AiProgressStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var detail = Detail(step.Detail);

        return step.Kind switch
        {
            AiProgressKind.Embedding => "Reading your question",
            AiProgressKind.EmbeddingScreenshot => "Looking at your screenshot",
            AiProgressKind.PlanningSearch => "Working out what to search for",
            AiProgressKind.SearchingGuides => "Searching the guides",
            AiProgressKind.IdentifyingPlant => "Identifying the plant in the screenshot",
            AiProgressKind.LookingUpPlant => detail is null ? "Looking up where the plant grows" : $"Looking up where *{detail}* grows",
            AiProgressKind.DrawingRangeMap => detail is null ? "Drawing a range map" : $"Drawing a range map of *{detail}*",
            AiProgressKind.Thinking => "Thinking",
            AiProgressKind.TryingAnotherModel => "Asking another model",
            AiProgressKind.ThinkingAboutFindings => "Thinking about what it found",
            AiProgressKind.AnsweringWithWhatItHas => "Answering with what it has",
            AiProgressKind.SearchingGuidesFor => detail is null ? "Searching the guides" : $"Searching the guides for “{detail}”",
            AiProgressKind.ReadingGuide => detail is null ? "Reading a guide" : $"Reading *{detail}*",
            AiProgressKind.DrawingRegionMap => detail is null ? "Drawing a map of the regions" : $"Drawing a map of {detail}",
            AiProgressKind.ReadingChallenge => "Reading the challenge's results",
            AiProgressKind.PlacingRounds => "Placing the rounds and guesses",
            AiProgressKind.ReadingPanoramas => "Looking at the panoramas with GeoNN",
            AiProgressKind.Posting => "Posting the answer",
            _ => "Working"
        };
    }

    /// <summary>One line, not too long, with nothing in it Discord would read as formatting.</summary>
    private static string? Detail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        var line = string.Join(' ', detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length > MaxDetailLength)
        {
            line = line[..(MaxDetailLength - 1)].TrimEnd() + "…";
        }

        return Format.Sanitize(line);
    }
}

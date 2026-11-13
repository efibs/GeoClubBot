using System.Globalization;
using System.Text;
using Constants;
using Discord;
using UseCases.UseCases.AI.Quiz;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

/// <summary>The quiz message: the question, the answer behind a spoiler, and the buttons. Pure, so the shape is pinned by tests.</summary>
public static class AiQuizFormatter
{
    /// <summary>Longest answer shown; a slide's notes can run to pages, and the spoiler is read at a glance.</summary>
    public const int MaxAnswerLength = 500;

    /// <summary>The question above the picture.</summary>
    public static string Question(QuizQuestion question, string? askedCountry)
    {
        ArgumentNullException.ThrowIfNull(question);

        var builder = new StringBuilder();
        builder.Append("🧩 **Guide quiz**");
        if (!string.IsNullOrWhiteSpace(askedCountry))
        {
            builder.Append(" · ").Append(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(askedCountry.Trim()));
        }

        builder.AppendLine();
        builder.AppendLine(askedCountry is null
            ? "Where is this, and what gives it away? Decide, then open the spoiler."
            : "What does this picture tell you? Decide, then open the spoiler.");
        builder.AppendLine();

        var answer = new StringBuilder();
        if (question.CountryName is not null)
        {
            answer.Append("**").Append(EscapeFormatting(question.CountryName)).Append("** · ");
        }

        answer.Append(EscapeFormatting(question.Guide));
        if (!string.IsNullOrWhiteSpace(question.Section))
        {
            answer.Append(" › ").Append(EscapeFormatting(question.Section));
        }

        var text = Truncate(question.Answer, MaxAnswerLength);
        if (!string.IsNullOrWhiteSpace(text))
        {
            answer.AppendLine().Append(EscapeFormatting(text));
        }

        answer.AppendLine().Append("Source: <").Append(question.Clue.SourceUrl).Append('>');

        // One spoiler around the whole answer: the country, the guide and its link all give it away.
        builder.Append("||").Append(answer.ToString().Replace("||", "│", StringComparison.Ordinal)).Append("||");
        return builder.ToString();
    }

    /// <summary>What a member is told after judging their answer.</summary>
    public static string Score(QuizScore score, bool right)
    {
        ArgumentNullException.ThrowIfNull(score);

        var verdict = right ? "Nice one!" : "Next time.";
        var streak = score.Streak switch
        {
            0 => "No streak",
            1 => "Streak: 1",
            _ => $"Streak: {score.Streak.ToString(CultureInfo.InvariantCulture)} 🔥"
        };

        return $"{verdict} {streak} · {score.Right.ToString(CultureInfo.InvariantCulture)} of {score.Answered.ToString(CultureInfo.InvariantCulture)} right since the last restart"
               + (score.Best > 1 ? $" · best streak {score.Best.ToString(CultureInfo.InvariantCulture)}" : string.Empty);
    }

    /// <summary>The buttons under a question: judge yourself, and ask for another with the same filters.</summary>
    public static MessageComponent Buttons(string? askedCountry, bool lesserKnownOnly) =>
        new ComponentBuilder()
            .WithButton("I got it", ComponentIds.AiQuizRightButtonId, ButtonStyle.Success, new Emoji("✅"))
            .WithButton("I missed", ComponentIds.AiQuizWrongButtonId, ButtonStyle.Secondary, new Emoji("❌"))
            .WithButton("Another", AgainId(askedCountry, lesserKnownOnly), ButtonStyle.Primary, new Emoji("▶️"))
            .Build();

    /// <summary>The "another" button's id, carrying the filters: <c>ai-quiz-again:{lesser}:{country}</c>, within Discord's 100 characters.</summary>
    public static string AgainId(string? askedCountry, bool lesserKnownOnly)
    {
        var country = (askedCountry ?? string.Empty).Trim().Replace(':', ' ');
        var room = 100 - ComponentIds.AiQuizAgainButtonId.Length - 4;
        if (country.Length > room)
        {
            country = country[..room];
        }

        return $"{ComponentIds.AiQuizAgainButtonId}:{(lesserKnownOnly ? "1" : "0")}:{country}";
    }

    private static string Truncate(string text, int maxLength)
    {
        var flat = text.Trim();
        if (flat.Length <= maxLength)
        {
            return flat;
        }

        var cut = flat.LastIndexOf(' ', maxLength - 1);
        return $"{flat[..(cut > maxLength / 2 ? cut : maxLength - 1)].TrimEnd()}…";
    }

    private static string EscapeFormatting(string text) =>
        text.Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("~", "\\~", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("||", "│", StringComparison.Ordinal);
}

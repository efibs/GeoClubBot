using System.Globalization;
using System.Text;
using Constants;
using Discord;
using Entities;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Quiz;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

/// <summary>
/// The quiz's messages (#491): the question with four answers, the private verdict on a guess, the
/// question once its answer is out, and the leaderboard. Pure, so every shape is pinned by tests.
/// </summary>
public static class AiQuizFormatter
{
    /// <summary>Longest explanation shown; a slide's notes can run to pages.</summary>
    public const int MaxExplanationLength = 500;

    /// <summary>Discord's limit on a button's label.</summary>
    private const int MaxLabelLength = 80;

    private static readonly string[] Letters = ["🇦", "🇧", "🇨", "🇩"];

    /// <summary>The question as posted, and as it stands while members guess.</summary>
    public static string Question(AiQuizRound round)
    {
        ArgumentNullException.ThrowIfNull(round);

        var builder = new StringBuilder();
        AppendHeading(builder, round);

        builder.AppendLine("Pick an answer below: one guess each, and only you see whether you were right.");
        builder.Append("-# ").Append(Tally(round)).Append(" · the answer is shown ")
            .Append(new TimestampTag(round.RevealAtUtc, TimestampTagStyles.Relative));
        return builder.ToString();
    }

    /// <summary>The question once its answer is out: the answer, how everyone did, and what the guide says.</summary>
    public static string Revealed(AiQuizRound round)
    {
        ArgumentNullException.ThrowIfNull(round);

        var builder = new StringBuilder();
        AppendHeading(builder, round);

        builder.Append("✅ **").Append(Escape(round.CorrectOption)).Append("** · ");
        builder.AppendLine(round.Guesses.Count switch
        {
            0 => "nobody guessed",
            var count => $"{round.RightGuesses.ToString(CultureInfo.InvariantCulture)} of {count.ToString(CultureInfo.InvariantCulture)} got it"
        });

        AppendExplanation(builder, round);
        return builder.ToString();
    }

    /// <summary>What a member is told, privately, after guessing.</summary>
    public static string Verdict(QuizGuessOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var round = outcome.Round;
        var builder = new StringBuilder();
        if (outcome.Guess.Correct)
        {
            builder.Append("✅ Right: **").Append(Escape(round.CorrectOption)).Append("**! +")
                .Append(Points(outcome.Guess.Points)).AppendLine(".");
        }
        else
        {
            builder.Append("❌ Not quite: you picked **").Append(Escape(round.Options[outcome.Guess.OptionIndex]))
                .Append("**, it is **").Append(Escape(round.CorrectOption)).AppendLine("**.");
        }

        AppendExplanation(builder, round);
        builder.AppendLine();
        builder.Append("-# You: ").Append(Record(outcome.AllTime)).Append(" all time · ")
            .Append(Record(outcome.Recent)).Append(" in the last ")
            .Append(outcome.RecentDays.ToString(CultureInfo.InvariantCulture)).Append(" days. Keep it to yourself until the answer is out!");
        return builder.ToString();
    }

    /// <summary>The buttons under a question: the four answers, then reveal and another; once revealed, the answers disabled with the right one green.</summary>
    public static MessageComponent Buttons(AiQuizRound round)
    {
        ArgumentNullException.ThrowIfNull(round);

        var answers = new ActionRowBuilder();
        for (var index = 0; index < round.Options.Count; index++)
        {
            var style = round.IsRevealed && index == round.CorrectIndex ? ButtonStyle.Success : ButtonStyle.Secondary;
            answers.WithButton(new ButtonBuilder()
                .WithLabel(Label(round.Options[index]))
                .WithEmote(new Emoji(Letters[index]))
                .WithCustomId(PickId(round.Id, index))
                .WithStyle(style)
                .WithDisabled(round.IsRevealed));
        }

        var actions = new ActionRowBuilder();
        if (!round.IsRevealed)
        {
            actions.WithButton("Reveal", RevealId(round.Id), ButtonStyle.Secondary, new Emoji("🔓"));
        }

        actions.WithButton("Another", AgainId(round.AskedCountry, round.LesserKnownOnly, round.AskedDifficulty), ButtonStyle.Primary, new Emoji("▶️"));

        return new ComponentBuilder().AddRow(answers).AddRow(actions).Build();
    }

    /// <summary>The leaderboard: all time and the recent window, and where the member stands.</summary>
    public static string Leaderboard(QuizLeaderboard board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var builder = new StringBuilder();
        builder.AppendLine("🏆 **Quiz leaderboard**");
        builder.AppendLine("-# A right answer scores its difficulty: easy 1, medium 2, hard 3.");
        builder.AppendLine();
        builder.AppendLine("**All time**");
        AppendBoard(builder, board.AllTime);
        builder.AppendLine();
        builder.Append("**Last ").Append(board.RecentDays.ToString(CultureInfo.InvariantCulture)).AppendLine(" days**");
        AppendBoard(builder, board.Recent);
        builder.AppendLine();

        builder.Append("You: ").Append(Standing(board.Player)).Append(" all time · ")
            .Append(Standing(board.PlayerRecent)).Append(" in the last ")
            .Append(board.RecentDays.ToString(CultureInfo.InvariantCulture)).Append(" days");
        return builder.ToString();
    }

    /// <summary>A pick button's id: <c>ai-quiz-pick:{roundId}:{option}</c>.</summary>
    public static string PickId(Guid roundId, int option) =>
        $"{ComponentIds.AiQuizPickButtonId}:{roundId:N}:{option.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The reveal button's id: <c>ai-quiz-reveal:{roundId}</c>.</summary>
    public static string RevealId(Guid roundId) => $"{ComponentIds.AiQuizRevealButtonId}:{roundId:N}";

    /// <summary>
    /// The "another" button's id, carrying the filters: <c>ai-quiz-again:{lesser}:{difficulty}:{country}</c>,
    /// the difficulty 0 when left to the quiz, within Discord's 100 characters.
    /// </summary>
    public static string AgainId(string? askedCountry, bool lesserKnownOnly, AiQuizDifficulty? difficulty)
    {
        var country = (askedCountry ?? string.Empty).Trim().Replace(':', ' ');
        var room = 100 - ComponentIds.AiQuizAgainButtonId.Length - 6;
        if (country.Length > room)
        {
            country = country[..room];
        }

        var level = difficulty is { } d ? ((int)d).ToString(CultureInfo.InvariantCulture) : "0";
        return $"{ComponentIds.AiQuizAgainButtonId}:{(lesserKnownOnly ? "1" : "0")}:{level}:{country}";
    }

    /// <summary>
    /// Reads back what <see cref="AgainId"/> wrote after <c>ai-quiz-again:</c>. A button from before #491
    /// carries <c>{lesser}:{country}</c> without a difficulty, and still draws.
    /// </summary>
    public static (string? Country, bool LesserKnownOnly, AiQuizDifficulty? Difficulty) ReadAgainFilters(string filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var separator = filters.IndexOf(':', StringComparison.Ordinal);
        var lesser = separator < 0 ? filters : filters[..separator];
        var rest = separator < 0 ? string.Empty : filters[(separator + 1)..];

        AiQuizDifficulty? difficulty = null;
        if (rest.Length >= 2 && rest[1] == ':' && rest[0] is >= '0' and <= '3')
        {
            difficulty = rest[0] == '0' ? null : (AiQuizDifficulty)(rest[0] - '0');
            rest = rest[2..];
        }

        var country = rest.Trim();
        return (country.Length == 0 ? null : country, lesser == "1", difficulty);
    }

    /// <summary>Reads a pick button's id after <c>ai-quiz-pick:</c>; null when it is no pick.</summary>
    public static (Guid RoundId, int Option)? ReadPick(string roundId, string option) =>
        Guid.TryParseExact(roundId, "N", out var id)
        && int.TryParse(option, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? (id, index)
            : null;

    public static string DifficultyLabel(AiQuizDifficulty difficulty) => difficulty switch
    {
        AiQuizDifficulty.Easy => "🟢 Easy",
        AiQuizDifficulty.Medium => "🟡 Medium",
        _ => "🔴 Hard"
    };

    /// <summary>What is asked, in words: the country question, or a question within the country.</summary>
    public static string Ask(AiQuizRound round)
    {
        ArgumentNullException.ThrowIfNull(round);

        var country = Escape(round.CountryName ?? "this country");
        return round.Kind switch
        {
            AiQuizQuestionKind.Region => $"Where in {country} was this picture taken?",
            AiQuizQuestionKind.Clue => $"What in this picture is typical of {country}?",
            _ => "Which country is this?"
        };
    }

    private static void AppendHeading(StringBuilder builder, AiQuizRound round)
    {
        builder.Append("🧩 **Guide quiz**");
        if (round.Kind != AiQuizQuestionKind.Country && round.CountryName is not null)
        {
            builder.Append(" · ").Append(Escape(round.CountryName));
        }

        builder.Append(" · ").Append(DifficultyLabel(round.Difficulty))
            .Append(" (").Append(Points((int)round.Difficulty)).AppendLine(")");
        builder.Append("**").Append(Ask(round)).AppendLine("**");
    }

    private static void AppendExplanation(StringBuilder builder, AiQuizRound round)
    {
        var explanation = Truncate(round.Explanation, MaxExplanationLength);
        if (explanation.Length > 0)
        {
            builder.Append("> ").AppendLine(Escape(explanation));
        }

        builder.Append("-# ").Append(Escape(round.Guide));
        if (!string.IsNullOrWhiteSpace(round.Section))
        {
            builder.Append(" › ").Append(Escape(round.Section));
        }

        builder.Append(" · <").Append(round.SourceUrl).AppendLine(">");
    }

    private static void AppendBoard(StringBuilder builder, IReadOnlyList<QuizScoreLine> lines)
    {
        if (lines.Count == 0)
        {
            builder.AppendLine("Nobody has scored yet.");
            return;
        }

        var rank = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            // Ties share a rank: a member's rank is one more than those with more points.
            if (index == 0 || lines[index].Points != lines[index - 1].Points)
            {
                rank = index + 1;
            }

            var line = lines[index];
            var medal = rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => $"{rank.ToString(CultureInfo.InvariantCulture)}." };
            builder.Append(medal).Append(" <@").Append(line.UserId.ToString(CultureInfo.InvariantCulture)).Append("> · ")
                .AppendLine(Record(line));
        }
    }

    private static string Standing((QuizScoreLine Line, int Rank)? standing) =>
        standing is { } s
            ? $"#{s.Rank.ToString(CultureInfo.InvariantCulture)} with {Record(s.Line)}"
            : "no answers";

    private static string Record(QuizScoreLine line) =>
        $"{Points(line.Points)} ({line.Right.ToString(CultureInfo.InvariantCulture)} of {line.Answered.ToString(CultureInfo.InvariantCulture)} right)";

    private static string Points(int points) =>
        points == 1 ? "1 point" : $"{points.ToString(CultureInfo.InvariantCulture)} points";

    private static string Tally(AiQuizRound round) => round.Guesses.Count switch
    {
        0 => "nobody has guessed yet",
        var count => $"{count.ToString(CultureInfo.InvariantCulture)} guessed · {round.RightGuesses.ToString(CultureInfo.InvariantCulture)} right"
    };

    private static string Label(string option) =>
        option.Length <= MaxLabelLength ? option : $"{option[..(MaxLabelLength - 1)]}…";

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

    private static string Escape(string text) =>
        text.Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("~", "\\~", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Entities;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Tools;

namespace UseCases.UseCases.AI.Quiz;

/// <summary>A question the model wrote for a picture, checked: the right answer, three distinct wrong ones, a difficulty.</summary>
public sealed record RatedQuizQuestion(AiQuizQuestionKind Kind, string Answer, IReadOnlyList<string> Decoys, AiQuizDifficulty Difficulty);

/// <param name="Questions">The questions the picture fairly makes; empty when it makes none.</param>
/// <param name="Understood">Whether the reply was the form asked for at all; a reply that was not is no rating and is asked again later.</param>
public sealed record QuizRating(IReadOnlyList<RatedQuizQuestion> Questions, bool Understood)
{
    public static readonly QuizRating NotUnderstood = new([], false);
}

/// <summary>
/// What a vision model is asked about a quiz picture (#491), and what is made of its reply. Pure, so the
/// prompt and every check on the reply are pinned by tests.
///
/// The quiz once asked of any captioned picture "where is this?". It showed a guide's header card (the
/// flag, the outline, ".lv"), a map with the town names on it, and a car with nothing else in sight. The
/// model is shown what the player will see — the picture alone — and what the guide says of it, and
/// asked which questions are fair. It also writes the wrong answers, since only it knows which countries
/// or regions the picture could be mistaken for.
/// </summary>
public static partial class QuizRatingPrompt
{
    /// <summary>Low, so one picture is rated much the same whichever run rates it.</summary>
    public const double Temperature = 0.2;

    /// <summary>
    /// Longest answer kept. Discord cuts a button's label at 80 characters, and the option's letter goes
    /// in front.
    /// </summary>
    public const int MaxOptionLength = 70;

    public const string SystemPrompt = """
        You write multiple-choice questions for a GeoGuessr quiz. The player sees ONE picture from a
        community guide and four answers to pick from. The player does NOT see the guide's words: you are
        given them only so you know what the picture is about.

        Judge three possible questions and answer each "no" unless it is fair:

        1. Country: "Where is this?" Fair only when a practised player could narrow the picture to the
           country and a few look-alikes from what is visible. Answer "no" when the picture gives the
           country away (its name, flag, outline or map, internet domain, or a label naming it) and when
           it shows too little to tell (a car alone, plain asphalt, generic sky or trees, a diagram that
           means nothing without the guide's words).
        2. Region: "Where in <country> is this?" Fair only when the guide's words tie the picture to one
           part of the country (a region, province, state, "the north"). Answer "no" when the clue holds
           for the whole country, and when the picture names the region (place names on a map, a labelled
           area).
        3. Clue: "What in this picture is typical of <country>?" Fair when the thing the guide points to
           can be seen in the picture. Answer "no" when it cannot be seen, or the picture writes it out.
           The right answer is a thing to recognise, never text printed in the picture (a road number,
           a place or shop name).

        Difficulty: easy = most regular players know it; medium = known to players who study; hard =
        obscure, or easy to confuse.

        Wrong answers must be plausible AND wrong: for a country, countries that look alike or border it;
        for a region, other parts of the same country named in the same style; for a clue, things of the
        same kind as the right answer (another tree for a tree, another pole or road marking for a pole)
        that a player might think of, but that do NOT tell this country. Never the sky, clouds, the road
        itself or anything else every picture shows, which no player would pick. A wrong
        answer must never be anything the guide's words mention, nor anything else typical of the
        country, since a player who picks it would be right. Never repeat the right answer among them.
        Keep every answer under 60 characters.
        """;

    /// <summary>The messages for one picture: the task, then what the guide says of it and the picture itself.</summary>
    /// <param name="countryName">The country the guide files the picture under, as a reader would name it.</param>
    /// <param name="image">The picture as the model receives it: inline when the relay holds a copy, by URL otherwise.</param>
    public static IReadOnlyList<AiChatMessage> BuildMessages(KnowledgeHit hit, string countryName, string image)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryName);

        var section = SectionLabel.Clean(hit.SectionPath);
        var title = hit.Title?.Trim() ?? string.Empty;
        if (title.Length > 0 && section.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            section = section[title.Length..].TrimStart(' ', '>', '—').Trim();
        }

        var text = new StringBuilder();
        text.Append("Country: ").AppendLine(countryName);
        if (title.Length > 0)
        {
            text.Append("Guide: ").AppendLine(title);
        }

        if (section.Length > 0)
        {
            text.Append("Section: ").AppendLine(section);
        }

        text.Append("The guide says: ").AppendLine(Flatten(hit.Text, 800));
        if (hit.HasGeneratedCaption)
        {
            text.Append("(A model described the picture as: ").Append(Flatten(hit.GeneratedCaption!, 400)).AppendLine(")");
        }

        text.AppendLine();
        text.AppendLine("Answer in exactly this form, one line each, nothing else:");
        text.AppendLine("Country shown: yes or no (is the country's name, flag, outline or domain in the picture?)");
        text.AppendLine("Region shown: yes or no (is a region or town named in the picture?)");
        text.AppendLine("Country: easy, medium, hard or no");
        text.AppendLine("Country wrong: three countries, separated by ;");
        text.AppendLine("Region: easy, medium, hard or no");
        text.AppendLine("Region answer: the part of the country the picture is from");
        text.AppendLine("Region wrong: three other parts of the country, separated by ;");
        text.AppendLine("Clue: easy, medium, hard or no");
        text.AppendLine("Clue answer: the thing in the picture that is typical, in a few words");
        text.Append("Clue wrong: three other things, separated by ;");

        return
        [
            AiChatMessage.System(SystemPrompt),
            AiChatMessage.User(text.ToString(), [image])
        ];
    }

    /// <summary>
    /// Reads a reply into the questions it makes, each checked: the country question's wrong answers must
    /// be three real countries other than the right one, and every answer short enough for a button. A
    /// question the model judged fair but wrote badly is left out; the picture's other questions stand.
    /// </summary>
    /// <param name="countryCode">The ISO code of the country the picture is filed under, the country question's answer.</param>
    public static QuizRating Parse(string? reply, string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        if (string.IsNullOrWhiteSpace(reply)
            || GuardrailVerdictDetector.IsVerdict(reply)
            || GarbledTextDetector.IsGarbled(reply))
        {
            return QuizRating.NotUnderstood;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in reply.Split('\n'))
        {
            var line = Markup().Replace(raw, string.Empty).Trim();
            if (Field().Match(line) is { Success: true } field)
            {
                var key = Spaces().Replace(field.Groups["key"].Value.Trim(), " ");
                fields.TryAdd(key, field.Groups["value"].Value.Trim());
            }
        }

        var verdicts = new[] { "Country", "Region", "Clue" };
        if (!verdicts.Any(fields.ContainsKey))
        {
            return QuizRating.NotUnderstood;
        }

        var countryShown = IsYes(fields.GetValueOrDefault("Country shown"));
        var regionShown = IsYes(fields.GetValueOrDefault("Region shown"));

        var questions = new List<RatedQuizQuestion>(3);

        if (!countryShown && ReadDifficulty(fields.GetValueOrDefault("Country")) is { } countryDifficulty)
        {
            var decoys = ReadCountries(fields.GetValueOrDefault("Country wrong"), countryCode);
            if (decoys.Count >= 3)
            {
                questions.Add(new RatedQuizQuestion(AiQuizQuestionKind.Country, IsoCountries.NameOf(countryCode), [.. decoys.Take(3)], countryDifficulty));
            }
        }

        if (!regionShown && ReadDifficulty(fields.GetValueOrDefault("Region")) is { } regionDifficulty
            && ReadChoices(fields.GetValueOrDefault("Region answer"), fields.GetValueOrDefault("Region wrong")) is { } region)
        {
            questions.Add(new RatedQuizQuestion(AiQuizQuestionKind.Region, region.Answer, region.Decoys, regionDifficulty));
        }

        if (ReadDifficulty(fields.GetValueOrDefault("Clue")) is { } clueDifficulty
            && ReadChoices(fields.GetValueOrDefault("Clue answer"), fields.GetValueOrDefault("Clue wrong")) is { } clue)
        {
            questions.Add(new RatedQuizQuestion(AiQuizQuestionKind.Clue, clue.Answer, clue.Decoys, clueDifficulty));
        }

        return new QuizRating(questions, true);
    }

    private static bool IsYes(string? value) =>
        value is not null && value.TrimStart().StartsWith("yes", StringComparison.OrdinalIgnoreCase);

    private static AiQuizDifficulty? ReadDifficulty(string? value)
    {
        var word = FirstWord().Match(value ?? string.Empty).Value.ToLowerInvariant();
        return word switch
        {
            "easy" => AiQuizDifficulty.Easy,
            "medium" => AiQuizDifficulty.Medium,
            "hard" => AiQuizDifficulty.Hard,
            _ => null
        };
    }

    /// <summary>The countries named, as their table names, each once and never the right answer.</summary>
    private static List<string> ReadCountries(string? value, string countryCode)
    {
        var codes = new List<string>(3);
        foreach (var part in Split(value))
        {
            var code = IsoCountries.Resolve(part, IsoCountries.Gazetteer);
            if (code is not null
                && !string.Equals(code, countryCode, StringComparison.OrdinalIgnoreCase)
                && !codes.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                codes.Add(code);
            }
        }

        return [.. codes.Select(IsoCountries.NameOf)];
    }

    /// <summary>A right answer and three distinct wrong ones, each short enough for a button; null when the model wrote fewer.</summary>
    private static (string Answer, IReadOnlyList<string> Decoys)? ReadChoices(string? answer, string? wrong)
    {
        var right = Option(answer);
        if (right is null)
        {
            return null;
        }

        var decoys = new List<string>(3);
        foreach (var part in Split(wrong))
        {
            var decoy = Option(part);
            if (decoy is not null
                && !string.Equals(decoy, right, StringComparison.OrdinalIgnoreCase)
                && !decoys.Contains(decoy, StringComparer.OrdinalIgnoreCase))
            {
                decoys.Add(decoy);
            }
        }

        return decoys.Count >= 3 ? (right, decoys.Take(3).ToList()) : null;
    }

    /// <summary>An answer as a button shows it: one line, no trailing full stop, a capital first; null when empty or too long.</summary>
    private static string? Option(string? value)
    {
        var flat = Spaces().Replace(value ?? string.Empty, " ").Trim().Trim('"', '“', '”', '\'').TrimEnd('.').Trim();
        if (flat.Length == 0 || flat.Length > MaxOptionLength || flat.Equals("no", StringComparison.OrdinalIgnoreCase)
            || flat.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return char.IsLower(flat[0])
            ? string.Concat(char.ToUpper(flat[0], CultureInfo.InvariantCulture).ToString(), flat[1..])
            : flat;
    }

    private static IEnumerable<string> Split(string? value) =>
        (value ?? string.Empty).Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(part => part.Contains(',', StringComparison.Ordinal) && !value!.Contains(';', StringComparison.Ordinal)
                ? part.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [part])
            .Select(part => LeadingEnumeration().Replace(part, string.Empty));

    private static string Flatten(string text, int maxLength)
    {
        var flat = Spaces().Replace(text, " ").Trim();
        return flat.Length <= maxLength ? flat : $"{flat[..maxLength].TrimEnd()}…";
    }

    /// <summary>"Country: hard", "- **Region answer:** Northeast" — a label and its value.</summary>
    [GeneratedRegex(@"^[\-\*•\d\.\)\s]*(?<key>[A-Za-z][A-Za-z ]{1,20}?)\s*:\s*(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Field();

    [GeneratedRegex(@"\*\*|__|`", RegexOptions.CultureInvariant)]
    private static partial Regex Markup();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[A-Za-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex FirstWord();

    /// <summary>"1. ", "a) ", "- " in front of a listed answer.</summary>
    [GeneratedRegex(@"^(?:\d+[\.\)]|[a-c][\.\)]|[\-•])\s*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LeadingEnumeration();
}

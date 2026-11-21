using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;

namespace UseCases.UseCases.AI.StreetView;

/// <summary>A clue from the guides that was seen in the pictures.</summary>
/// <param name="Index">Which of the candidates it is, from zero.</param>
/// <param name="Where">What the model said it saw, and in which picture.</param>
public sealed record SeenClue(int Index, string Where);

/// <summary>What a vision model said it saw: the guides' clues it could point at, and anything else telling.</summary>
/// <param name="Seen">The candidates it saw, in their order.</param>
/// <param name="AlsoSeen">Other things in the pictures that help place them, in its words.</param>
/// <param name="BestPicture">The picture that shows the most telling clue, from zero; null when it named none.</param>
/// <param name="Judged">How many candidates it gave a verdict on, seen or not.</param>
public sealed record ClueSightVerdict(IReadOnlyList<SeenClue> Seen, IReadOnlyList<string> AlsoSeen, int? BestPicture, int Judged)
{
    /// <summary>The reply said something usable: a verdict on a clue, or something it saw.</summary>
    public bool IsUsable => Judged > 0 || AlsoSeen.Count > 0;
}

/// <summary>
/// What a free vision model is asked about a Street View place, and how its reply is read. Pure, so the
/// wording is pinned by a test.
///
/// Asked to describe a panorama, a model names what the country is known for rather than what is
/// there; and the review that knew only the country listed clues the round never showed (#480). So
/// the model is handed the guides' own clues for the place, one numbered line each, and asked which it
/// can point at: a checklist it answers line by line is something a free model does reliably, and the
/// clues it confirms are the guides' words, which the review can cite. "Seen:" lines catch what the
/// list missed. It is told the country, because the guides' clues are that country's, and told that
/// most of them will not be there.
/// </summary>
public static partial class ClueSightPrompt
{
    public const double Temperature = 0.0;

    /// <summary>A candidate is cut to this many characters: enough to say what to look for.</summary>
    private const int MaxCandidateLength = 300;

    /// <summary>"Seen:" lines kept at most.</summary>
    private const int MaxAlsoSeen = 6;

    /// <summary>One "Seen:" line is cut to this many characters.</summary>
    private const int MaxNoteLength = 200;

    public const string SystemPrompt = """
        You check GeoGuessr rounds. You are shown Street View pictures of one place and a numbered list
        of clues from community guides about where it is. Say which clues you can actually see in the
        pictures.

        Be strict. A clue counts only when you can point at it in a picture: say which picture and
        where. Knowing the country does not make its clues visible, and most clues will not be in the
        pictures, so "no" is the usual answer. When something is too small, blurred or far to make
        out, answer "unclear". Licence plates and faces are blurred in Street View.
        """;

    /// <summary>The messages for one place: the instructions, then the pictures with the clues to look for.</summary>
    /// <param name="pictures">The pictures, as URLs the provider can read (<c>data:</c> URLs), in the order of <paramref name="angles"/>.</param>
    public static IReadOnlyList<AiChatMessage> BuildMessages(
        IReadOnlyList<string> pictures,
        IReadOnlyList<StreetViewAngle> angles,
        string countryName,
        string? region,
        string? guessedCountryName,
        IReadOnlyList<KnowledgeHit> candidates)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(angles);
        ArgumentNullException.ThrowIfNull(candidates);

        var text = new StringBuilder();
        text.Append(pictures.Count == 1 ? "The picture is one view" : $"The {pictures.Count.ToString(CultureInfo.InvariantCulture)} pictures are views")
            .Append(" of one Street View place in ").Append(countryName);
        if (!string.IsNullOrWhiteSpace(region))
        {
            text.Append(" (").Append(region).Append(')');
        }

        text.Append(pictures.Count == 1 ? ", the one view the player saw" : string.Empty).Append(". ");
        if (pictures.Count > 1)
        {
            text.Append("Looking ")
                .Append(string.Join(", ", angles.Take(pictures.Count).Select((angle, index) =>
                    $"picture {(index + 1).ToString(CultureInfo.InvariantCulture)} {Compass(angle.Heading)}")))
                .Append('.');
        }

        text.AppendLine();
        if (!string.IsNullOrWhiteSpace(guessedCountryName))
        {
            text.Append("The player guessed ").Append(guessedCountryName).AppendLine(".");
        }

        text.AppendLine();
        if (candidates.Count > 0)
        {
            text.AppendLine("Clues from the guides:");
            for (var index = 0; index < candidates.Count; index++)
            {
                text.Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").AppendLine(Describe(candidates[index]));
            }

            text.AppendLine();
            text.AppendLine("Answer in exactly this form, one line per clue, in their order:");
            text.AppendLine("1: yes — picture 2, left roadside: a white bollard with a black band");
            text.AppendLine("2: no");
            text.AppendLine("3: unclear — picture 1, the sign is too far to read");
        }

        text.AppendLine("Then up to five lines that start \"Seen:\", naming other things in the pictures that help tell the country or region "
                        + "(road lines and their colours, bollards, poles, guardrails, signs and their script, kerbs, vegetation, soil, terrain, "
                        + "buildings, the Google car), each with its picture number.");
        text.Append("Last, one line: \"Best picture: n\", the picture that shows the most telling clue.");

        return
        [
            AiChatMessage.System(SystemPrompt),
            AiChatMessage.User(text.ToString(), pictures)
        ];
    }

    /// <summary>
    /// Reads the reply: a "yes" for a numbered clue is a sighting; "no", "unclear" and anything else is
    /// not. Numbers out of range and repeats are ignored, so a reply that wanders is read for what it
    /// does say.
    /// </summary>
    public static ClueSightVerdict Parse(string? reply, int candidateCount, int pictureCount)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return new ClueSightVerdict([], [], null, 0);
        }

        var seen = new List<SeenClue>();
        var judged = new HashSet<int>();
        var notes = new List<string>();
        int? best = null;

        foreach (var raw in reply.Split('\n'))
        {
            var line = raw.Replace("**", string.Empty, StringComparison.Ordinal).Replace("__", string.Empty, StringComparison.Ordinal).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (BestLine().Match(line) is { Success: true } bestMatch)
            {
                if (int.TryParse(bestMatch.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var picture)
                    && picture >= 1 && picture <= pictureCount)
                {
                    best = picture - 1;
                }

                continue;
            }

            if (SeenLine().Match(line) is { Success: true } note)
            {
                var what = PictureFirst().Replace(note.Groups["what"].Value.Trim().TrimEnd('.'), "picture ${n}: ");
                if (what.Length > 0 && notes.Count < MaxAlsoSeen && !IsNothing(what))
                {
                    notes.Add(Cut(what, MaxNoteLength));
                }

                continue;
            }

            if (VerdictLine().Match(line) is not { Success: true } verdict
                || !int.TryParse(verdict.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number < 1 || number > candidateCount
                || !judged.Add(number))
            {
                continue;
            }

            var word = verdict.Groups["verdict"].Value.ToLowerInvariant();
            if (word is "yes" or "visible" or "seen")
            {
                var where = verdict.Groups["rest"].Value.Trim().TrimStart('—', '–', '-', ':', ',').Trim();
                seen.Add(new SeenClue(number - 1, Cut(where, MaxNoteLength)));
            }
        }

        return new ClueSightVerdict([.. seen.OrderBy(clue => clue.Index)], notes, best, judged.Count);
    }

    /// <summary>A candidate as the model reads it: its heading, then what the guide says, or what a model said its picture shows.</summary>
    public static string Describe(KnowledgeHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);

        var label = SectionLabel.Clean(hit.SectionPath);
        var body = hit.IsHeadingOnlyPicture && hit.HasGeneratedCaption
            ? hit.GeneratedCaption!
            : hit.HasGeneratedCaption && hit.Kind == KnowledgeChunkKind.Image
                ? $"{hit.Text} (the picture shows: {hit.GeneratedCaption})"
                : hit.Text;

        var flat = Whitespace().Replace(body, " ").Trim();
        var text = string.IsNullOrWhiteSpace(label) || flat.StartsWith(label, StringComparison.OrdinalIgnoreCase)
            ? flat
            : $"{label}: {flat}";
        return Cut(text, MaxCandidateLength);
    }

    /// <summary>"north-east", for a heading in degrees.</summary>
    public static string Compass(double heading)
    {
        string[] points = ["north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"];
        var normalised = ((heading % 360) + 360) % 360;
        return points[(int)Math.Round(normalised / 45, MidpointRounding.AwayFromZero) % 8];
    }

    private static bool IsNothing(string what) =>
        what.Equals("none", StringComparison.OrdinalIgnoreCase)
        || what.Equals("nothing", StringComparison.OrdinalIgnoreCase)
        || what.Equals("n/a", StringComparison.OrdinalIgnoreCase);

    private static string Cut(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)].TrimEnd() + "…";

    /// <summary>"3: yes — picture 2, …", "[3] Yes, picture 2", "3. unclear", "Clue 3: no".</summary>
    [GeneratedRegex(@"^(?:[-*•]\s*)?(?:clue\s*)?\[?(?<n>\d{1,2})\]?\s*[:.)\-–—]?\s*(?<verdict>yes|no|unclear|unsure|maybe|partly|partially|visible|not visible|seen|not seen)\b(?<rest>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex VerdictLine();

    [GeneratedRegex(@"^(?:[-*•]\s*)?(?:also\s+)?seen\s*:\s*(?<what>.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SeenLine();

    [GeneratedRegex(@"^(?:[-*•]\s*)?best\s+picture\s*[:\-–—]?\s*(?:picture\s*)?(?<n>\d{1,2})\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BestLine();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    /// <summary>"1 Yellow dashed centre line", "(2) a sign": a note that starts with its picture's number alone.</summary>
    [GeneratedRegex(@"^\(?(?<n>\d)\)?[:.]?\s+(?=\p{L})", RegexOptions.CultureInvariant)]
    private static partial Regex PictureFirst();
}

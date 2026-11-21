using System.Globalization;
using System.Text;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;

namespace UseCases.UseCases.AI.Review;

/// <param name="Number">The round's number, from one.</param>
/// <param name="CountryCode">Where the round was, ISO 3166 alpha-2; null when it could not be placed.</param>
/// <param name="Region">The first-level region it was in, when known.</param>
/// <param name="GuessCountryCode">Where the player guessed, ISO 3166 alpha-2; null for no guess or one that could not be placed.</param>
/// <param name="GuessRegion">The region of the guess, when known.</param>
/// <param name="DistanceKm">How far the guess was off; null without a guess.</param>
/// <param name="Points">The round's score.</param>
/// <param name="Reading">What a Street View model made of the round's panorama; null without one.</param>
public sealed record ReviewedRound(
    int Number,
    string? CountryCode,
    string? Region,
    string? GuessCountryCode,
    string? GuessRegion,
    double? DistanceKm,
    int? Points,
    PanoramaReading? Reading = null)
{
    /// <summary>
    /// A round someone played. False for a Street View place a member sent on its own, which has no
    /// guess to judge and nothing to miss.
    /// </summary>
    public bool Played { get; init; } = true;

    /// <summary>The panorama, as the game showed it; null when the round names none.</summary>
    public RoundPanorama? Panorama { get; init; }

    /// <summary>What the round was seen to show, by the numbers its excerpts are offered under; null until it is looked at.</summary>
    public RoundEvidence? Evidence { get; init; }

    /// <summary>The player guessed another country, or nothing.</summary>
    public bool MissedCountry =>
        Played && CountryCode is not null && !string.Equals(CountryCode, GuessCountryCode, StringComparison.OrdinalIgnoreCase);

    public string CountryName => CountryCode is null ? "an unknown place" : IsoCountries.NameOf(CountryCode);

    public string? GuessCountryName => GuessCountryCode is null ? null : IsoCountries.NameOf(GuessCountryCode);

    /// <summary>"Spain (Valencian Community)", or the country alone.</summary>
    public string PlaceName => string.IsNullOrWhiteSpace(Region) ? CountryName : $"{CountryName} ({Region})";
}

/// <summary>What GeoNN read a round's panorama as: the countries it weighed, likeliest first, and how far its guess was off.</summary>
public sealed record PanoramaReading(IReadOnlyList<CountryProbability> Countries, double DistanceKm, string Model)
{
    /// <summary>The country it weighed most, ISO 3166 alpha-2; null when it named none.</summary>
    public string? TopCountryCode => Countries.Count == 0 ? null : Countries[0].CountryCode;
}

/// <summary>A round's panorama: which one, the views it is read in, and a link that opens it.</summary>
/// <param name="PanoId">Google's panorama id.</param>
/// <param name="Angles">The views that show what the player saw.</param>
/// <param name="MapsUrl">Opens Street View at the round in Google Maps.</param>
public sealed record RoundPanorama(string PanoId, IReadOnlyList<StreetViewAngle> Angles, string MapsUrl);

/// <summary>What a round was seen to show, as the vision check found it.</summary>
/// <param name="Checked">A vision model looked at the round's panorama and answered.</param>
/// <param name="Seen">The excerpts it showed, by number, the country's clues before the region's.</param>
/// <param name="ToLookFor">Numbers of excerpts of clues the guides give for the place that it did not show (or that could not be checked).</param>
/// <param name="AlsoSeen">Other things the model saw, in its words: not from the guides.</param>
public sealed record RoundEvidence(
    bool Checked,
    IReadOnlyList<SeenExcerpt> Seen,
    IReadOnlyList<int> ToLookFor,
    IReadOnlyList<string> AlsoSeen);

/// <param name="Marker">The number the excerpt is offered under.</param>
/// <param name="Where">Where in the pictures the model saw it.</param>
/// <param name="Regional">Found for the round's region rather than its country.</param>
public sealed record SeenExcerpt(int Marker, string Where, bool Regional);

/// <summary>What a review reviews.</summary>
public enum ReviewSubjectKind
{
    /// <summary>The member's own game of a challenge.</summary>
    Challenge,

    /// <summary>A single game, whoever played it.</summary>
    Game,

    /// <summary>One Street View place a member sent.</summary>
    StreetView
}

/// <param name="Kind">What is reviewed.</param>
/// <param name="Token">The challenge's or game's token; null for a Street View place.</param>
/// <param name="PlayerNick">Whose game it is; null for a Street View place.</param>
/// <param name="IsOwnGame">The member asking played it, so the review speaks to them.</param>
/// <param name="MapName">The map the game was played on, when GeoGuessr says.</param>
public sealed record ReviewSubject(ReviewSubjectKind Kind, string? Token, string? PlayerNick, bool IsOwnGame, string? MapName = null)
{
    /// <summary>"challenge `AbC…`", "fibs's game `AbC…`", "the Street View place".</summary>
    public string Describe() => Kind switch
    {
        ReviewSubjectKind.Challenge => $"challenge `{Token}`",
        ReviewSubjectKind.Game => IsOwnGame || PlayerNick is null ? $"game `{Token}`" : $"{PlayerNick}'s game `{Token}`",
        _ => "the Street View place"
    };
}

/// <summary>
/// The question the model is asked for a review: the rounds, where the player was and guessed, the
/// clues each round was seen to show, and how to write it up. Pure, so the wording is pinned by a test.
///
/// Two lessons from #480 shape it. Told only a round's country, the model listed the country's clues,
/// most of which "weren't even in the round", so each round now names the excerpts it was seen to show,
/// and nothing else is offered as shown. And a missed easy round (Spain taken for Portugal, with Spain's
/// yellow guardrail reflectors in plain view) was answered with "very regional and hard to remember
/// clues", so a missed round asks for the easiest country-wide clue first and one regional at most.
/// </summary>
public static class RoundReviewPrompt
{
    public static string Build(IReadOnlyList<ReviewedRound> rounds, ReviewSubject subject, int wellPlayedPoints)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        ArgumentNullException.ThrowIfNull(subject);

        return subject.Kind == ReviewSubjectKind.StreetView
            ? BuildPlace(rounds, wellPlayedPoints)
            : BuildGame(rounds, subject, wellPlayedPoints);
    }

    private static string BuildGame(IReadOnlyList<ReviewedRound> rounds, ReviewSubject subject, int wellPlayedPoints)
    {
        var text = new StringBuilder();
        var map = string.IsNullOrWhiteSpace(subject.MapName) ? string.Empty : $" on {subject.MapName}";
        text.AppendLine(subject.IsOwnGame
            ? $"Review my GeoGuessr game ({subject.Describe()}{map}), round by round."
            : $"Review this GeoGuessr game, played by {subject.PlayerNick}{map} ({subject.Describe()}), round by round.");
        text.AppendLine();

        foreach (var round in rounds)
        {
            AppendRound(text, round, wellPlayedPoints);
        }

        text.AppendLine();
        text.AppendLine("How to write the review:");
        text.AppendLine(subject.IsOwnGame
            ? "- Speak to me as \"you\"."
            : $"- This is {subject.PlayerNick}'s game, not mine: speak of {subject.PlayerNick}.");
        AppendShownRules(text, rounds);
        text.AppendLine("- A round with the wrong country: first the one or two easiest, most reliable clues it was seen to show that hold "
                        + "across the whole country, and how they tell it apart from the country guessed. Then one regional clue at most. "
                        + "When it was seen to show none, say so plainly and give the clue to look for next time.");
        text.AppendLine($"- A round in the right country with {wellPlayedPoints.ToString("N0", CultureInfo.InvariantCulture)} points or more (\"well played\"): one line.");
        text.AppendLine("- Any other round in the right country: the clues it was seen to show that narrow down the region.");
        text.AppendLine("- Keep each round to a few sentences, under a bold \"Round n\", and end with the two or three things to practise first.");
        AppendGeoNnRule(text, rounds);

        return text.ToString().TrimEnd();
    }

    private static string BuildPlace(IReadOnlyList<ReviewedRound> rounds, int wellPlayedPoints)
    {
        var text = new StringBuilder();
        var place = rounds.Count > 0 ? rounds[0] : null;
        text.AppendLine($"Explain this Street View place: what gives away its country and region. It is in {place?.PlaceName ?? "an unknown place"}.");
        text.AppendLine();
        if (place is not null)
        {
            AppendRound(text, place, wellPlayedPoints);
        }

        text.AppendLine();
        text.AppendLine("How to write the explanation:");
        AppendShownRules(text, rounds);
        text.AppendLine("- First the clues it was seen to show that tell the country, the easiest and most reliable first; then those that tell the region.");
        text.AppendLine("- When it was seen to show none of the guides' clues, say so plainly, then give the clues to look for.");
        text.AppendLine("- Keep it to a short paragraph or a few bullet points.");
        AppendGeoNnRule(text, rounds);

        return text.ToString().TrimEnd();
    }

    private static void AppendRound(StringBuilder text, ReviewedRound round, int wellPlayedPoints)
    {
        if (round.Played)
        {
            text.Append("Round ").Append(round.Number.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(round.PlaceName).Append(". ");
            if (round.GuessCountryCode is null && round.DistanceKm is null)
            {
                text.Append("No guess — wrong country.");
            }
            else
            {
                text.Append("Guessed ").Append(round.GuessCountryName ?? "somewhere");
                if (!string.IsNullOrWhiteSpace(round.GuessRegion))
                {
                    text.Append(" (").Append(round.GuessRegion).Append(')');
                }

                if (round.DistanceKm is { } km)
                {
                    text.Append(", ").Append(Kilometres(km)).Append(" km away");
                }

                if (round.Points is { } points)
                {
                    text.Append(", ").Append(points.ToString("N0", CultureInfo.InvariantCulture)).Append(" points");
                }

                text.Append(round.MissedCountry
                    ? " — wrong country."
                    : round.Points >= wellPlayedPoints
                        ? " — right country, well played."
                        : " — right country.");
            }
        }
        else
        {
            text.Append("The place: ").Append(round.PlaceName).Append('.');
        }

        if (round.Reading is { Countries.Count: > 0 } reading)
        {
            text.Append(" GeoNN read the panorama as ")
                .Append(string.Join(", ", reading.Countries.Select(country =>
                    $"{IsoCountries.NameOf(country.CountryCode)} {Math.Round(country.Probability * 100).ToString("0", CultureInfo.InvariantCulture)}%")))
                .Append(" (its guess ").Append(Kilometres(reading.DistanceKm)).Append(" km off).");
        }

        text.AppendLine();

        var evidence = round.Evidence;
        if (evidence is null || !evidence.Checked)
        {
            text.Append("  Its panorama could not be looked at, so nothing is known of what it showed.");
            if (evidence is { ToLookFor.Count: > 0 })
            {
                text.Append(" Clues of ").Append(round.CountryName).Append(" to look for: ").Append(Markers(evidence.ToLookFor)).Append('.');
            }

            text.AppendLine();
            return;
        }

        if (evidence.Seen.Count == 0)
        {
            text.AppendLine("  Seen in its panorama: none of the guides' clues.");
        }
        else
        {
            text.Append("  Seen in its panorama: ")
                .Append(string.Join("; ", evidence.Seen.Select(seen =>
                    $"[{seen.Marker.ToString(CultureInfo.InvariantCulture)}]{(seen.Regional ? " (regional)" : string.Empty)}"
                    + (string.IsNullOrWhiteSpace(seen.Where) ? string.Empty : $" {seen.Where}"))))
                .AppendLine(".");
        }

        if (evidence.AlsoSeen.Count > 0)
        {
            text.Append("  Also seen, not from the guides: ").Append(string.Join("; ", evidence.AlsoSeen)).AppendLine(".");
        }

        if (evidence.ToLookFor.Count > 0)
        {
            text.Append("  Not seen in it, but clues of ").Append(round.CountryName).Append(" to look for: ")
                .Append(Markers(evidence.ToLookFor)).AppendLine(".");
        }
    }

    private static void AppendShownRules(StringBuilder text, IReadOnlyList<ReviewedRound> rounds)
    {
        text.AppendLine("- A vision model looked at each panorama and checked it against the guides. Say a round showed a clue only when "
                        + "it is listed as seen in it, and cite that number. A clue \"to look for\" was not in the round: offer it only "
                        + "as what to look for next time, never as something that was there.");
        // Without excerpts (a failed search, 2026-11-21) the model named France's motorway signs from its own
        // knowledge, wrongly and unmarked: the rule is said here, where the review is written, not only in
        // the system prompt.
        text.AppendLine("- Every sentence that names a clue either cites a listed excerpt or starts \"Not from the guides:\""
                        + (rounds.Any(round => round.Evidence is { AlsoSeen.Count: > 0 })
                            ? ", the vision model's own notes (\"not from the guides\" above) included. "
                            : ". ")
                        + "A marker covers only its own sentence.");
    }

    private static void AppendGeoNnRule(StringBuilder text, IReadOnlyList<ReviewedRound> rounds)
    {
        if (rounds.Any(round => round.Reading is { Countries.Count: > 0 }))
        {
            text.AppendLine(
                "- GeoNN is a model trained on Street View that looked at each round's panorama. Use it only to say how "
                + "plainly a round showed its country: sure and right means the clues were there to see; unsure, or "
                + "wrong, means the round was hard, so do not blame the player for it. It is not one of the excerpts: never cite "
                + "it, and never name a clue it did not come with.");
        }
    }

    private static string Markers(IEnumerable<int> markers) =>
        string.Join(", ", markers.Select(marker => $"[{marker.ToString(CultureInfo.InvariantCulture)}]"));

    private static string Kilometres(double km) =>
        km < 10 ? km.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(km).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// What the guides are searched for, per round: clues to check the panorama against. The country's
    /// most reliable clues, its road furniture and its poles and signs (the easy, country-wide clues a
    /// missed round wants), what tells it from the country guessed, and its region's. All of one review's
    /// searches go out in one embedding request.
    /// </summary>
    public static IReadOnlyList<RoundSearch> Searches(ReviewedRound round)
    {
        ArgumentNullException.ThrowIfNull(round);

        var country = round.CountryName;
        var searches = new List<RoundSearch>
        {
            new($"How to recognise {country}: its most reliable clues, seen all over the country", RoundSearchKind.Country),
            new($"{country}: road lines, bollards, guardrails and kerbs", RoundSearchKind.Country),
            new($"{country}: utility poles, road signs and their script", RoundSearchKind.Country)
        };

        if (round.MissedCountry && round.GuessCountryName is { } guessed)
        {
            searches.Add(new RoundSearch($"How to tell {country} apart from {guessed}", RoundSearchKind.Contrast));
        }

        if (!string.IsNullOrWhiteSpace(round.Region))
        {
            searches.Add(new RoundSearch($"How to recognise {round.Region}, {country}: distinctive clues of the region", RoundSearchKind.Region));
        }

        return searches;
    }
}

/// <summary>What a round's search looks for.</summary>
public enum RoundSearchKind
{
    /// <summary>The country's clues.</summary>
    Country,

    /// <summary>What tells the country from the one guessed.</summary>
    Contrast,

    /// <summary>The region's clues.</summary>
    Region
}

/// <param name="Text">What is embedded and searched for, within the round's country.</param>
/// <param name="Kind">What it looks for.</param>
public sealed record RoundSearch(string Text, RoundSearchKind Kind);

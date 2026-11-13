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
    /// <summary>The player guessed another country, or nothing.</summary>
    public bool MissedCountry =>
        CountryCode is not null && !string.Equals(CountryCode, GuessCountryCode, StringComparison.OrdinalIgnoreCase);

    public string CountryName => CountryCode is null ? "an unknown place" : IsoCountries.NameOf(CountryCode);

    public string? GuessCountryName => GuessCountryCode is null ? null : IsoCountries.NameOf(GuessCountryCode);
}

/// <summary>What GeoNN read a round's panorama as: the countries it weighed, likeliest first, and how far its guess was off.</summary>
public sealed record PanoramaReading(IReadOnlyList<CountryProbability> Countries, double DistanceKm, string Model)
{
    /// <summary>The country it weighed most, ISO 3166 alpha-2; null when it named none.</summary>
    public string? TopCountryCode => Countries.Count == 0 ? null : Countries[0].CountryCode;
}

/// <summary>
/// The question the model is asked for a review: the rounds, where the player was and guessed, and
/// what to do with the excerpts. Pure, so the wording is pinned by a test.
/// </summary>
public static class RoundReviewPrompt
{
    public static string Build(IReadOnlyList<ReviewedRound> rounds, string challengeToken)
    {
        ArgumentNullException.ThrowIfNull(rounds);

        var text = new StringBuilder();
        text.AppendLine($"Review my GeoGuessr game (challenge {challengeToken}), round by round.");
        text.AppendLine();

        foreach (var round in rounds)
        {
            text.Append("Round ").Append(round.Number.ToString(CultureInfo.InvariantCulture)).Append(": ")
                .Append(round.CountryName);
            if (!string.IsNullOrWhiteSpace(round.Region))
            {
                text.Append(" (").Append(round.Region).Append(')');
            }

            text.Append(". ");
            if (round.GuessCountryCode is null && round.DistanceKm is null)
            {
                text.Append("I made no guess.");
            }
            else
            {
                text.Append("I guessed ").Append(round.GuessCountryName ?? "somewhere");
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

                text.Append(round.MissedCountry ? " — wrong country." : " — right country.");
            }

            if (round.Reading is { Countries.Count: > 0 } reading)
            {
                text.Append(" GeoNN read the panorama as ")
                    .Append(string.Join(", ", reading.Countries.Select(country =>
                        $"{IsoCountries.NameOf(country.CountryCode)} {Math.Round(country.Probability * 100).ToString("0", CultureInfo.InvariantCulture)}%")))
                    .Append(" (its guess ").Append(Kilometres(reading.DistanceKm)).Append(" km off).");
            }

            text.AppendLine();
        }

        text.AppendLine();
        text.AppendLine(
            "For each round, name the clues from the excerpts that would have told me the country, and the region "
            + "where the excerpts say so; cite them. For the rounds where I had the wrong country, say what sets the "
            + "right country apart from the one I guessed, and what to study. Keep each round to a few sentences, "
            + "and end with the two or three things to practise first.");

        if (rounds.Any(round => round.Reading is { Countries.Count: > 0 }))
        {
            text.AppendLine(
                "GeoNN is a model trained on Street View that looked at each round's panorama. Use it only to say how "
                + "plainly a round showed its country: sure and right means the clues were there to see; unsure, or "
                + "wrong, means the round was hard, so do not blame me for it. It is not one of the excerpts: never cite "
                + "it, and never name a clue it did not come with.");
        }

        return text.ToString().TrimEnd();
    }

    private static string Kilometres(double km) =>
        km < 10 ? km.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(km).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>What the guides are searched for, per round: the country's distinctive clues, and its region's.</summary>
    public static string SearchText(ReviewedRound round)
    {
        ArgumentNullException.ThrowIfNull(round);

        return string.IsNullOrWhiteSpace(round.Region)
            ? $"How to recognise {round.CountryName}: distinctive clues"
            : $"How to recognise {round.Region}, {round.CountryName}: distinctive clues of the region";
    }
}

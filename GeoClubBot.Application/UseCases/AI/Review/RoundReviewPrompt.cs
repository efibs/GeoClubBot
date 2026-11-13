using System.Globalization;
using System.Text;
using UseCases.UseCases.AI.Tools;

namespace UseCases.UseCases.AI.Review;

/// <param name="Number">The round's number, from one.</param>
/// <param name="CountryCode">Where the round was, ISO 3166 alpha-2; null when it could not be placed.</param>
/// <param name="Region">The first-level region it was in, when known.</param>
/// <param name="GuessCountryCode">Where the player guessed, ISO 3166 alpha-2; null for no guess or one that could not be placed.</param>
/// <param name="GuessRegion">The region of the guess, when known.</param>
/// <param name="DistanceKm">How far the guess was off; null without a guess.</param>
/// <param name="Points">The round's score.</param>
public sealed record ReviewedRound(
    int Number,
    string? CountryCode,
    string? Region,
    string? GuessCountryCode,
    string? GuessRegion,
    double? DistanceKm,
    int? Points)
{
    /// <summary>The player guessed another country, or nothing.</summary>
    public bool MissedCountry =>
        CountryCode is not null && !string.Equals(CountryCode, GuessCountryCode, StringComparison.OrdinalIgnoreCase);

    public string CountryName => CountryCode is null ? "an unknown place" : IsoCountries.NameOf(CountryCode);

    public string? GuessCountryName => GuessCountryCode is null ? null : IsoCountries.NameOf(GuessCountryCode);
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
                    text.Append(", ").Append(km < 10 ? km.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(km).ToString("N0", CultureInfo.InvariantCulture)).Append(" km away");
                }

                if (round.Points is { } points)
                {
                    text.Append(", ").Append(points.ToString("N0", CultureInfo.InvariantCulture)).Append(" points");
                }

                text.Append(round.MissedCountry ? " — wrong country." : " — right country.");
            }

            text.AppendLine();
        }

        text.AppendLine();
        text.AppendLine(
            "For each round, name the clues from the excerpts that would have told me the country, and the region "
            + "where the excerpts say so; cite them. For the rounds where I had the wrong country, say what sets the "
            + "right country apart from the one I guessed, and what to study. Keep each round to a few sentences, "
            + "and end with the two or three things to practise first.");

        return text.ToString().TrimEnd();
    }

    /// <summary>What the guides are searched for, per round: the country's distinctive clues, and its region's.</summary>
    public static string SearchText(ReviewedRound round)
    {
        ArgumentNullException.ThrowIfNull(round);

        return string.IsNullOrWhiteSpace(round.Region)
            ? $"How to recognise {round.CountryName}: distinctive clues"
            : $"How to recognise {round.Region}, {round.CountryName}: distinctive clues of the region";
    }
}

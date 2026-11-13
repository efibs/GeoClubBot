using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// Finds which of a country's regions a text names: "Lạng Sơn and Quảng Ninh", "Lang Son", "the Yukon",
/// "Sabah and Sarawak". Matched without accents or case, as whole words, longest name first, so
/// "North Sumatra" is not "Sumatra" and "Niedersachsen" is found as Lower Saxony by its alias.
///
/// A handful of region names are plain words — Ghana's Central, Northern and Western regions, Nepal's
/// Central, several countries' Capital — which an answer uses in their ordinary sense far more often
/// than as a name. Those are matched only when written with a capital and followed or preceded by a
/// word that makes them a region ("Central Region", "the Northern Region of Ghana").
///
/// Pure, so every rule is pinned by a test.
/// </summary>
public static partial class RegionMention
{
    /// <summary>Shorter names are too often another word: Ohio and Utah pass, "Ba" and "Fa" do not.</summary>
    private const int MinNameLength = 4;

    /// <summary>Names that are ordinary words, taken only with "region", "province", "state" or the like beside them.</summary>
    // Stryker disable once all : lookup data
    private static readonly HashSet<string> GenericNames = new(StringComparer.Ordinal)
    {
        "central", "centre", "center", "north", "south", "east", "west", "northern", "southern", "eastern", "western",
        "north east", "north west", "south east", "south west", "northeast", "northwest", "southeast", "southwest",
        "north eastern", "north western", "south eastern", "south western", "capital", "coast", "coastal", "upper",
        "lower", "middle", "interior", "highlands", "lowlands", "island", "islands", "far north", "far west", "far east",
        "border", "littoral", "plateau", "savanes", "maritime", "eastern cape", "western cape", "northern cape",
        "north central", "south central", "volta", "east coast", "west coast", "north coast", "south coast"
    };

    private static readonly string[] RegionWords = ["region", "province", "state", "county", "district", "department", "prefecture", "governorate", "oblast", "division", "territory", "area", "zone"];

    /// <summary>The regions of <paramref name="regions"/> that <paramref name="text"/> names, in the order they first appear, none twice.</summary>
    public static IReadOnlyList<RegionName> Find(string? text, IReadOnlyList<RegionName> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);

        if (string.IsNullOrWhiteSpace(text) || regions.Count == 0)
        {
            return [];
        }

        var haystack = $" {Normalise(text)} ";
        var found = new List<(int Position, RegionName Region)>();

        foreach (var region in regions)
        {
            var position = -1;
            foreach (var name in region.AllNames.Select(Normalise).Where(name => name.Length >= MinNameLength).OrderByDescending(name => name.Length))
            {
                var at = GenericNames.Contains(name) ? FindGeneric(haystack, name) : haystack.IndexOf($" {name} ", StringComparison.Ordinal);
                if (at >= 0 && (position < 0 || at < position))
                {
                    position = at;
                }
            }

            if (position >= 0)
            {
                found.Add((position, region));
            }
        }

        // A longer name that contains a shorter one wins the place: "North Sumatra" is not Sumatra.
        var kept = new List<(int Position, RegionName Region)>();
        foreach (var candidate in found.OrderByDescending(entry => LongestName(entry.Region)).ThenBy(entry => entry.Position))
        {
            var overlaps = kept.Any(other =>
                other.Position <= candidate.Position && candidate.Position < other.Position + LongestName(other.Region) + 2);
            if (!overlaps)
            {
                kept.Add(candidate);
            }
        }

        return [.. kept.OrderBy(entry => entry.Position).Select(entry => entry.Region)];
    }

    /// <summary>A generic name counts only beside a word that says it is a region: "central region", "province of central".</summary>
    private static int FindGeneric(string haystack, string name)
    {
        foreach (var word in RegionWords)
        {
            var after = haystack.IndexOf($" {name} {word} ", StringComparison.Ordinal);
            if (after >= 0)
            {
                return after;
            }

            var before = haystack.IndexOf($" {word} of {name} ", StringComparison.Ordinal);
            if (before >= 0)
            {
                return before;
            }
        }

        return -1;
    }

    private static int LongestName(RegionName region) => region.AllNames.Select(name => Normalise(name).Length).DefaultIfEmpty(0).Max();

    /// <summary>Lower case, accents dropped, anything but letters and digits a single space.</summary>
    public static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // Đ and đ do not decompose; they are the one letter Vietnamese names need by hand.
            builder.Append(character switch
            {
                'Đ' or 'đ' => 'd',
                'ß' => 's',
                'Ø' or 'ø' => 'o',
                'Æ' or 'æ' => 'a',
                'Ł' or 'ł' => 'l',
                _ => char.ToLowerInvariant(character)
            });
        }

        return Spaces().Replace(NotAWord().Replace(builder.ToString(), " "), " ").Trim();
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotAWord();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}

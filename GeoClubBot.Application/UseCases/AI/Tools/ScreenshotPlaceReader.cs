using System.Globalization;
using System.Text.RegularExpressions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Retrieval;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// Reads where a member says a screenshot was taken: coordinates or a Google Maps link, a town ("just
/// south of Boston", "near Quincy, MA"), a state or province ("in Massachusetts"), or a country ("this
/// is Bulgaria"). The most exact place the text supports wins, as long as the rest of the text agrees
/// with it: "Montana" is the Bulgarian province beside "Bulgaria", the US state otherwise.
///
/// Town names are everyday words too often — Reading, Normal, Spring, Paradise — so a town counts only
/// after a word that places something ("in", "near", "south of", "outside", "it's") or before one
/// ("Boston area"); a region also when written with a capital. A place followed by a plant word names
/// a plant, not where it stands: "Norway spruce", "Aleppo pine", "California redwood". A text naming two
/// countries and nothing more exact ("Bulgaria or Romania?") names no place: weighing by the wrong one is
/// worse than by none.
///
/// Pure, so every rule is pinned by a test.
/// </summary>
public static partial class ScreenshotPlaceReader
{
    /// <summary>A town is shown as near coordinates up to this far from them.</summary>
    private const double NearestTownKm = 60;

    /// <summary>A town this large is meant over a region of the same name: New York, Sofia, Mexico City.</summary>
    private const long CityPopulation = 1_000_000;

    /// <summary>Shorter region names are too often another word, as in <see cref="RegionMention"/>.</summary>
    private const int MinRegionNameLength = 4;

    /// <summary>Shorter town names are too often another word, even after "in": "in it", "in us".</summary>
    private const int MinTownNameLength = 3;

    /// <summary>Words that place what follows: "in Bulgaria", "near Boston", "outside Sofia".</summary>
    // Stryker disable once all : lookup data
    private static readonly HashSet<string> PlacingWords = new(StringComparer.Ordinal)
    {
        "in", "near", "nearby", "around", "outside", "from", "at", "between", "towards", "toward", "within"
    };

    /// <summary>Words before "of" that make it place what follows: "south of Boston", "outskirts of Sofia".</summary>
    // Stryker disable once all : lookup data
    private static readonly HashSet<string> OfWords = new(StringComparer.Ordinal)
    {
        "north", "south", "east", "west", "northeast", "northwest", "southeast", "southwest", "northeastern",
        "northwestern", "southeastern", "southwestern", "northern", "southern", "eastern", "western", "central",
        "outskirts", "suburbs", "outside", "part", "parts", "area", "region", "province", "state", "county", "city",
        "town", "village", "middle", "heart", "edge", "coast", "north east", "north west", "south east", "south west"
    };

    /// <summary>Words of a question, which never stand between a place's name and a plant word that is its own.</summary>
    // Stryker disable once all : lookup data
    private static readonly HashSet<string> QuestionWords = new(StringComparer.Ordinal)
    {
        "what", "which", "whats", "is", "are", "this", "that", "these", "those", "the", "a", "an", "of", "kind",
        "type", "sort", "any", "some", "and", "or", "with", "for", "to", "it", "its", "s", "here", "there", "my", "your"
    };

    /// <summary>Words after a place that make it one: "Boston area", "Sofia region".</summary>
    // Stryker disable once all : lookup data
    private static readonly HashSet<string> AreaWords = new(StringComparer.Ordinal)
    {
        "area", "region", "suburbs", "outskirts", "metro", "vicinity", "surroundings"
    };

    /// <summary>
    /// Words that are towns of 15,000 people or more somewhere, or regions, but in a question are the
    /// word, however written: "taken in Spring", "in March", "in the picture".
    /// </summary>
    // Stryker disable once all : lookup data
    private static readonly HashSet<string> NeverPlaces = new(StringComparer.Ordinal)
    {
        "january", "february", "march", "april", "may", "june", "july", "august", "september", "october",
        "november", "december", "spring", "summer", "autumn", "fall", "winter", "monday", "tuesday", "wednesday",
        "thursday", "friday", "saturday", "sunday", "the", "this", "that", "there", "here", "it", "its", "one",
        "street", "view", "front", "back", "game", "round", "picture", "photo", "screenshot", "image", "map",
        "coverage", "camera", "car", "time", "general", "home", "best", "town", "city", "village", "country"
    };

    /// <summary>
    /// Words that are towns somewhere but in lower case are almost always the word: "in reading", "nice
    /// trees", "in mobile". Written with a capital after "in" or "near" they are taken for the town.
    /// </summary>
    // Stryker disable once all : lookup data
    private static readonly HashSet<string> LowerCaseWords = new(StringComparer.Ordinal)
    {
        "mobile", "reading", "bath", "nice", "split", "orange", "normal", "independence", "paradise", "surprise",
        "temple", "commerce", "enterprise", "liberty", "hope", "union", "central", "center", "centre", "north",
        "south", "east", "west", "middle", "deal", "sale", "sun", "bay", "beach", "lake", "river", "valley",
        "mountain", "mountains", "hill", "hills", "forest", "park", "garden", "gardens", "school", "university",
        "college", "airport", "pine", "palm", "cedar", "oak", "olive", "lime", "cherry", "laurel", "magnolia",
        "victory", "industry", "progress", "ocean", "sea", "island", "islands", "coast", "desert", "plain",
        "plains", "field", "fields", "farm", "road", "highway", "bridge", "church", "market", "port", "harbor",
        "harbour", "station", "rose", "lily", "ivy", "holly", "hazel", "eagle", "mission", "pleasant"
    };

    /// <summary>
    /// The place <paramref name="text"/> says the screenshot is from, or else the latest of
    /// <paramref name="earlierTexts"/> (the member's earlier messages in the branch, oldest first) that
    /// says one; null when none does. Countries named in the earlier messages also settle which of two
    /// same-named towns the text means.
    /// </summary>
    public static ScreenshotPlace? Find(
        string? text,
        PlaceIndex places,
        CountryGazetteer countries,
        IReadOnlyList<string>? earlierTexts = null)
    {
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(countries);

        var earlier = earlierTexts ?? [];
        var earlierCountries = earlier
            .SelectMany(earlierText => Read(earlierText, places, countries))
            .Where(reading => reading.Kind == ReadingKind.Country)
            .Select(reading => reading.CountryCode!)
            .ToHashSet(StringComparer.Ordinal);

        if (FindIn(text, places, countries, earlierCountries) is { } place)
        {
            return place;
        }

        foreach (var earlierText in earlier.Reverse())
        {
            if (FindIn(earlierText, places, countries, earlierCountries) is { } earlierPlace)
            {
                return earlierPlace;
            }
        }

        return null;
    }

    private static ScreenshotPlace? FindIn(string? text, PlaceIndex places, CountryGazetteer countries, HashSet<string> contextCountries)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (FindCoordinates(text) is { } coordinates)
        {
            return AtCoordinates(coordinates.Latitude, coordinates.Longitude, coordinates.Said, places);
        }

        var readings = Read(text, places, countries);
        if (readings.Count == 0)
        {
            return null;
        }

        Reading? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var reading in readings)
        {
            var score = Score(reading, readings, contextCountries);
            if (score > bestScore)
            {
                best = reading;
                bestScore = score;
            }
        }

        // Every reading disagrees with another: two countries, or a town in neither of them.
        return best is null || bestScore < 0 ? null : ToPlace(best);
    }

    /// <summary>
    /// How well a reading fits the rest of the text: agreeing with the other places named counts most,
    /// then being exact (a town over a region over a country), then prominence (a larger town, a larger
    /// region). A reading the text contradicts goes below zero.
    /// </summary>
    private static double Score(Reading reading, IReadOnlyList<Reading> all, HashSet<string> contextCountries)
    {
        var others = all.Where(other => !other.Overlaps(reading)).ToList();
        var otherCountries = others.Where(other => other.Kind == ReadingKind.Country).Select(other => other.CountryCode!)
            .Concat(others.SelectMany(other => other.QualifierCountries))
            .ToHashSet(StringComparer.Ordinal);
        var otherRegions = others.Where(other => other.Kind == ReadingKind.Region).Select(other => other.Place!.RegionKey)
            .OfType<string>()
            .Concat(others.SelectMany(other => other.QualifierRegions))
            .ToHashSet(StringComparer.Ordinal);

        // A town is more exact than a region — unless the same words name a region too, and the town is
        // no city: "in montana" is the state, not the Bulgarian town; "New York" the city.
        var sharesNameWithRegion = all.Any(other => other.Kind == ReadingKind.Region && other.Start == reading.Start && other.Length == reading.Length);
        var score = reading.Kind switch
        {
            ReadingKind.Town when sharesNameWithRegion && reading.Place!.Population < CityPopulation => 1.5,
            ReadingKind.Town => 3d,
            ReadingKind.Region => 2d,
            _ => 1d
        };

        if (otherCountries.Count > 0)
        {
            score += otherCountries.Contains(reading.CountryCode!) ? 4 : -4;
        }
        else if (contextCountries.Count > 0)
        {
            score += contextCountries.Contains(reading.CountryCode!) ? 2 : -1;
        }

        if (reading.Kind == ReadingKind.Town && otherRegions.Count > 0)
        {
            score += reading.Place!.RegionKey is { } key && otherRegions.Contains(key) ? 4 : -2;
        }

        if (reading.QualifierCountries.Count > 0 || reading.QualifierRegions.Count > 0)
        {
            var agrees = reading.QualifierCountries.Contains(reading.CountryCode!)
                         || (reading.Place?.RegionKey is { } key && reading.QualifierRegions.Contains(key));
            score += agrees ? 6 : -8;
        }

        // "Georgia" is the country unless something else says it is the state.
        if (reading.Kind != ReadingKind.Country && reading.AlsoNamesCountry is { } country && country != reading.CountryCode)
        {
            score -= 3;
        }

        score += reading.Kind switch
        {
            ReadingKind.Town => Math.Log10(reading.Place!.Population + 1) / 10,
            ReadingKind.Region => Math.Min(0.5, (reading.Place!.Bounds?.Area ?? 0) / 1000),
            _ => 0
        };

        return score;
    }

    private static ScreenshotPlace ToPlace(Reading reading)
    {
        if (reading.Kind == ReadingKind.Country)
        {
            return new ScreenshotPlace(IsoCountries.NameOf(reading.CountryCode!), reading.CountryCode, PlacePrecision.Country, null, null, null, reading.Said);
        }

        var place = reading.Place!;
        var country = IsoCountries.NameOf(place.CountryCode);
        return place.Kind == KnownPlaceKind.Town
            ? new ScreenshotPlace(
                string.Join(", ", new[] { place.Name, place.RegionName, country }.Where(part => !string.IsNullOrWhiteSpace(part))),
                place.CountryCode, PlacePrecision.Point, place.Latitude, place.Longitude, null, reading.Said)
            : new ScreenshotPlace($"{place.Name}, {country}", place.CountryCode, PlacePrecision.Region, place.Latitude, place.Longitude, place.Bounds, reading.Said);
    }

    private static ScreenshotPlace AtCoordinates(double latitude, double longitude, string said, PlaceIndex places)
    {
        var shown = $"{latitude.ToString("0.0000", CultureInfo.InvariantCulture)}, {longitude.ToString("0.0000", CultureInfo.InvariantCulture)}";
        var town = places.NearestTown(latitude, longitude, NearestTownKm);

        return town is null
            ? new ScreenshotPlace(shown, null, PlacePrecision.Point, latitude, longitude, null, said) { ShortName = shown }
            : new ScreenshotPlace(
                $"{shown} (near {string.Join(", ", new[] { town.Name, town.RegionName, IsoCountries.NameOf(town.CountryCode) }.Where(part => !string.IsNullOrWhiteSpace(part)))})",
                town.CountryCode, PlacePrecision.Point, latitude, longitude, null, said)
            { ShortName = shown };
    }

    /// <summary>Every place the text could be naming, with what stands beside each.</summary>
    private static IReadOnlyList<Reading> Read(string? text, PlaceIndex places, CountryGazetteer countries)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var words = Tokenise(text);
        var normalised = words.Select(word => word.Text).ToList();
        var readings = new List<Reading>();

        for (var start = 0; start < words.Count; start++)
        {
            // Countries anywhere: "Bulgaria, what tree is this?" needs no "in".
            var named = countries.MatchAt(normalised, start, out var countryLength);
            var countryCode = named.Count == 1 ? IsoCountries.Resolve(named[0].Name, countries) : null;
            if (countryCode is not null && !NamesAPlant(text, words, start + countryLength))
            {
                readings.Add(new Reading(ReadingKind.Country, null, countryCode, start, countryLength, Said(text, words, start, countryLength), [], [], null));
            }

            for (var length = Math.Min(PlaceIndex.MaxNameWords, words.Count - start); length > 0; length--)
            {
                var name = string.Join(' ', normalised.Skip(start).Take(length));
                var found = places.Find(name);
                if (found.Count == 0)
                {
                    continue;
                }

                var end = start + length;
                var capitalised = words.Skip(start).Take(length).All(word => word.Capitalised);
                if (NamesAPlant(text, words, end)
                    || (length == 1 && (NeverPlaces.Contains(name) || (!capitalised && LowerCaseWords.Contains(name)))))
                {
                    break;
                }

                var placed = IsPlaced(normalised, start) || (end < words.Count && AreaWords.Contains(normalised[end]));
                var (qualifierCountries, qualifierRegions) = ReadQualifier(text, words, end, places, countries);
                var qualified = qualifierCountries.Count > 0 || qualifierRegions.Count > 0;
                var alsoCountry = countryLength == length ? countryCode : null;

                foreach (var place in found)
                {
                    var accepted = place.Kind == KnownPlaceKind.Town
                        ? name.Length >= MinTownNameLength && (placed || qualified)
                        : name.Length >= MinRegionNameLength
                          && (placed || capitalised || qualified)
                          && (!RegionMention.IsGenericName(name) || (end < words.Count && RegionMention.IsRegionWord(normalised[end])));

                    if (accepted)
                    {
                        readings.Add(new Reading(
                            place.Kind == KnownPlaceKind.Town ? ReadingKind.Town : ReadingKind.Region,
                            place, place.CountryCode, start, length, Said(text, words, start, length),
                            qualifierCountries, qualifierRegions, alsoCountry));
                    }
                }

                break;
            }
        }

        return readings;
    }

    /// <summary>
    /// What follows a place to say which one it is: "Boston, MA", "Quincy, Illinois", "Boston, USA".
    /// A two-letter code counts after a comma or in capitals only.
    /// </summary>
    private static (IReadOnlyList<string> Countries, IReadOnlyList<string> Regions) ReadQualifier(
        string text, IReadOnlyList<Word> words, int at, PlaceIndex places, CountryGazetteer countries)
    {
        if (at >= words.Count)
        {
            return ([], []);
        }

        var afterComma = text.AsSpan(words[at - 1].End, words[at].Start - words[at - 1].End).Contains(',');
        var next = words[at];

        if (next.Original.Length is 2 or 3 && (afterComma || next.Original.All(char.IsUpper)))
        {
            var byCode = places.FindRegionsByCode(next.Text);
            if (byCode.Count > 0)
            {
                return ([], [.. byCode.Select(region => region.RegionKey).OfType<string>()]);
            }
        }

        var normalised = words.Select(word => word.Text).ToList();
        var named = countries.MatchAt(normalised, at, out _);
        if (named.Count == 1 && IsoCountries.Resolve(named[0].Name, countries) is { } code && (afterComma || words[at].Capitalised))
        {
            return ([code], []);
        }

        if (!afterComma)
        {
            return ([], []);
        }

        for (var length = Math.Min(PlaceIndex.MaxNameWords, words.Count - at); length > 0; length--)
        {
            var regions = places.Find(string.Join(' ', normalised.Skip(at).Take(length)))
                .Where(place => place.Kind == KnownPlaceKind.Region && place.RegionKey is not null)
                .Select(place => place.RegionKey!)
                .ToList();
            if (regions.Count > 0)
            {
                return ([], regions);
            }
        }

        return ([], []);
    }

    /// <summary>Whether the words before <paramref name="start"/> place what follows: "in", "near", "south of", "it's".</summary>
    private static bool IsPlaced(IReadOnlyList<string> words, int start)
    {
        var before = start >= 1 ? words[start - 1] : null;
        var twoBefore = start >= 2 ? words[start - 2] : null;
        var threeBefore = start >= 3 ? words[start - 3] : null;

        return before switch
        {
            null => false,
            _ when PlacingWords.Contains(before) => true,
            "to" => twoBefore is "close" or "next" or "near",
            "of" => twoBefore is not null && (OfWords.Contains(twoBefore) || (threeBefore is not null && OfWords.Contains($"{threeBefore} {twoBefore}"))),
            "is" or "s" or "re" => twoBefore is "this" or "it" or "that" or "we" or "i" or "which",
            "its" or "thats" or "im" => true,
            _ => false
        };
    }

    /// <summary>
    /// Whether the words after a name make it part of a plant's: a plant word right after it ("Norway
    /// spruce", "Aleppo pine"), or after one more word that is not a question's ("Mexican fan palm",
    /// "Japanese black pine"). Punctuation ends the name: "Romania, what tree is this?" names a place.
    /// </summary>
    private static bool NamesAPlant(string text, IReadOnlyList<Word> words, int end)
    {
        if (end >= words.Count || Punctuated(text, words, end))
        {
            return false;
        }

        if (PlantIdentificationQuestion.IsPlantWord(words[end].Text))
        {
            return true;
        }

        return end + 1 < words.Count
               && !QuestionWords.Contains(words[end].Text)
               && !Punctuated(text, words, end + 1)
               && PlantIdentificationQuestion.IsPlantWord(words[end + 1].Text);
    }

    /// <summary>Whether a mark ends the phrase before word <paramref name="index"/>: a comma, a full stop, a question mark.</summary>
    private static bool Punctuated(string text, IReadOnlyList<Word> words, int index) =>
        index > 0 && text.AsSpan(words[index - 1].End, Math.Max(0, words[index].Start - words[index - 1].End)).IndexOfAny(",.;:?!()") >= 0;

    /// <summary>What the member wrote, with the words that placed it: "south of Boston".</summary>
    private static string Said(string text, IReadOnlyList<Word> words, int start, int length)
    {
        var from = start;
        while (from > 0 && start - from < 3
               && (PlacingWords.Contains(words[from - 1].Text) || words[from - 1].Text is "of" or "the" || OfWords.Contains(words[from - 1].Text)))
        {
            from--;
        }

        // "the" alone says nothing: "the usa" is "usa".
        while (from < start && words[from].Text == "the")
        {
            from++;
        }

        return text[words[from].Start..words[start + length - 1].End].Trim();
    }

    /// <summary>
    /// The text's words, normalised as places are indexed, with where each lies and how it was written.
    /// "US" in capitals is the United States, never the pronoun.
    /// </summary>
    private static List<Word> Tokenise(string text)
    {
        var words = new List<Word>();
        foreach (Match match in WordPattern().Matches(text))
        {
            if (match.Value is "US" or "USA")
            {
                words.Add(new Word("united", match.Value, match.Index, match.Index + match.Length, true));
                words.Add(new Word("states", match.Value, match.Index, match.Index + match.Length, true));
                continue;
            }

            foreach (var part in RegionMention.Normalise(match.Value).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                words.Add(new Word(part, match.Value, match.Index, match.Index + match.Length, char.IsUpper(match.Value[0])));
            }
        }

        return words;
    }

    /// <summary>
    /// The first coordinates in the text: a Google Maps or Street View link ("@42.2511,-71.0023,3a"),
    /// a decimal pair ("42.25, -71.00", "42.25N 71.00W"), or degrees and minutes ("42°15'N 71°0'W").
    /// </summary>
    public static (double Latitude, double Longitude, string Said)? FindCoordinates(string text)
    {
        foreach (var (pattern, isLink) in new[] { (MapsLink(), true), (DecimalPair(), false) })
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (!double.TryParse(match.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                    || !double.TryParse(match.Groups["lon"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
                {
                    continue;
                }

                // A bare pair needs the precision coordinates are copied with, or a hemisphere: "0.52, 0.31" are scores.
                var hemisphere = match.Groups["ns"].Success || match.Groups["ew"].Success;
                if (!isLink && !hemisphere && (Decimals(match.Groups["lat"].Value) < 3 || Decimals(match.Groups["lon"].Value) < 3))
                {
                    continue;
                }

                latitude = match.Groups["ns"].Value.Equals("S", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(latitude) : latitude;
                longitude = match.Groups["ew"].Value.Equals("W", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(longitude) : longitude;

                if (Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180 && (latitude != 0 || longitude != 0))
                {
                    return (latitude, longitude, match.Value.Trim());
                }
            }
        }

        foreach (Match match in DegreesMinutes().Matches(text))
        {
            var latitude = Degrees(match, "latd", "latm", "lats") * (match.Groups["ns"].Value.Equals("S", StringComparison.OrdinalIgnoreCase) ? -1 : 1);
            var longitude = Degrees(match, "lond", "lonm", "lons") * (match.Groups["ew"].Value.Equals("W", StringComparison.OrdinalIgnoreCase) ? -1 : 1);
            if (Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180)
            {
                return (latitude, longitude, match.Value.Trim());
            }
        }

        return null;
    }

    private static int Decimals(string number) => number.Length - number.IndexOf('.', StringComparison.Ordinal) - 1;

    private static double Degrees(Match match, string degrees, string minutes, string seconds)
    {
        static double Read(Group group) =>
            group.Success && double.TryParse(group.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

        return Read(match.Groups[degrees]) + (Read(match.Groups[minutes]) / 60) + (Read(match.Groups[seconds]) / 3600);
    }

    private enum ReadingKind
    {
        Country,
        Region,
        Town
    }

    /// <param name="AlsoNamesCountry">The country the same words name, when they name one too: "Georgia".</param>
    private sealed record Reading(
        ReadingKind Kind,
        KnownPlace? Place,
        string? CountryCode,
        int Start,
        int Length,
        string Said,
        IReadOnlyList<string> QualifierCountries,
        IReadOnlyList<string> QualifierRegions,
        string? AlsoNamesCountry)
    {
        public bool Overlaps(Reading other) => Start < other.Start + other.Length && other.Start < Start + Length;
    }

    private sealed record Word(string Text, string Original, int Start, int End, bool Capitalised);

    [GeneratedRegex(@"[\p{L}\p{M}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    /// <summary>"@42.2511,-71.0023,3a,75y" and "?q=42.25,-71.00" / "ll=" / "cbll=" / "viewpoint=" / "query=".</summary>
    [GeneratedRegex(@"(?:@|[?&](?:q|ll|cbll|viewpoint|query|center)=)(?<lat>-?\d{1,2}\.\d+)(?:,|%2C)\s*(?<lon>-?\d{1,3}\.\d+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MapsLink();

    /// <summary>"42.2511, -71.0023", "42.25 N, 71.00 W", "-33.865 151.209": a pair of decimals, checked further by <see cref="FindCoordinates"/>.</summary>
    [GeneratedRegex(@"(?<![\d.\w])(?<lat>-?\d{1,2}\.\d{2,})\s*°?(?:\s*(?<ns>[NS])(?!\p{L}))?\s*[,;/ ]\s*(?<lon>-?\d{1,3}\.\d{2,})\s*°?(?:\s*(?<ew>[EW])(?!\p{L}))?(?![\d.])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DecimalPair();

    /// <summary>"42°15'30\"N 71°0'W".</summary>
    [GeneratedRegex(@"(?<latd>\d{1,2})\s*°\s*(?:(?<latm>\d{1,2})\s*['′]\s*)?(?:(?<lats>\d{1,2}(?:\.\d+)?)\s*[""″]\s*)?(?<ns>[NS])(?!\p{L})[\s,;/]*(?<lond>\d{1,3})\s*°\s*(?:(?<lonm>\d{1,2})\s*['′]\s*)?(?:(?<lons>\d{1,2}(?:\.\d+)?)\s*[""″]\s*)?(?<ew>[EW])(?!\p{L})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DegreesMinutes();
}

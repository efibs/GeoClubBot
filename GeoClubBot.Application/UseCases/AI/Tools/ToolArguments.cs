using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Tools;

/// <summary>Which guides a search reads.</summary>
public enum GuideSource
{
    /// <summary>PlonkIt alone: what every player has read, searched to check whether it carries a clue.</summary>
    MainGuide,

    /// <summary>Every guide but PlonkIt: where a clue a player does not know yet is found.</summary>
    Others
}

/// <param name="Query">What to search for, trimmed to <see cref="ToolArguments.MaxQueryLength"/>.</param>
/// <param name="Country">The country to prefer, as the model wrote it, or null.</param>
/// <param name="Source">The guides to search, or null for all of them.</param>
public sealed record SearchGuidesArguments(string Query, string? Country, bool PicturesOnly, GuideSource? Source = null);

/// <param name="Excerpt">The marker of the excerpt whose guide to read.</param>
public sealed record ReadGuideArguments(int Excerpt);

/// <param name="Name">The species' scientific or common name, trimmed to <see cref="ToolArguments.MaxNameLength"/>.</param>
/// <param name="Country">The country whose regions to count, as the model wrote it, or null.</param>
public sealed record SpeciesRangeArguments(string Name, string? Country);

/// <param name="Name">The region as the model wrote it.</param>
/// <param name="Share">How common the thing is there, 0 to 1; 1 when the model gave none.</param>
public sealed record RegionShare(string Name, double Share);

/// <param name="Country">The country, as the model wrote it.</param>
/// <param name="Regions">The regions to highlight, at most <see cref="ToolArguments.MaxRegionsPerMap"/>.</param>
public sealed record RegionMapArguments(string Country, IReadOnlyList<RegionShare> Regions);

/// <summary>
/// Reads the arguments a model wrote for a tool call. They are the model's, not ours: missing, of the
/// wrong type, a number written as a string, an object written as a JSON string, far too long. Each
/// reader accepts what it reasonably can and otherwise returns a short message the model is shown in
/// place of a result — never an exception, which would cost the whole answer for one sloppy call.
///
/// Pure, so each of those shapes is pinned by a test.
/// </summary>
public static partial class ToolArguments
{
    /// <summary>A search is a phrase, not a paragraph; past this it is the question pasted back.</summary>
    public const int MaxQueryLength = 300;

    public const int MaxNameLength = 120;

    public const int MaxCountryLength = 80;

    public static bool TryReadSearchGuides(string? json, out SearchGuidesArguments arguments, out string error)
    {
        arguments = new SearchGuidesArguments(string.Empty, null, false);
        if (!TryReadObject(json, out var root, out error))
        {
            error = Problem(AnswerTools.SearchGuides, error, """{"query": "pole supports", "country": "Brazil"}""");
            return false;
        }

        var query = ReadText(root, "query", MaxQueryLength);
        if (query is null)
        {
            error = Problem(AnswerTools.SearchGuides, "'query' is required", """{"query": "pole supports", "country": "Brazil"}""");
            return false;
        }

        arguments = new SearchGuidesArguments(
            query,
            ReadText(root, "country", MaxCountryLength),
            ReadFlag(root, "pictures_only"),
            ReadSource(ReadText(root, "source", MaxCountryLength)));
        return true;
    }

    /// <summary>
    /// The guides a search names, however the model put it: "plonkit" and "plonk it" are PlonkIt,
    /// "other", "others" and "not plonkit" the rest. Anything else searches every guide, as no value does:
    /// a source the bot cannot read is not worth refusing a search for.
    /// </summary>
    private static GuideSource? ReadSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var named = string.Concat(source.Where(char.IsLetter)).ToLowerInvariant();
        return named switch
        {
            "plonkit" or "plonkitnet" or "plonkitonly" or "onlyplonkit" => GuideSource.MainGuide,
            "other" or "others" or "notplonkit" or "nonplonkit" or "noplonkit" or "withoutplonkit" or "exceptplonkit" => GuideSource.Others,
            _ => null
        };
    }

    public static bool TryReadReadGuide(string? json, out ReadGuideArguments arguments, out string error)
    {
        arguments = new ReadGuideArguments(0);
        if (!TryReadObject(json, out var root, out error))
        {
            error = Problem(AnswerTools.ReadGuide, error, """{"excerpt": 3}""");
            return false;
        }

        if (ReadNumber(root, "excerpt") is not { } excerpt || excerpt < 1)
        {
            error = Problem(AnswerTools.ReadGuide, "'excerpt' must be the number of one of the excerpts", """{"excerpt": 3}""");
            return false;
        }

        arguments = new ReadGuideArguments(excerpt);
        return true;
    }

    public static bool TryReadSpeciesRange(string? json, out SpeciesRangeArguments arguments, out string error)
    {
        arguments = new SpeciesRangeArguments(string.Empty, null);
        if (!TryReadObject(json, out var root, out error))
        {
            error = Problem(AnswerTools.SpeciesRange, error, """{"name": "Pinus kesiya"}""");
            return false;
        }

        var name = ReadText(root, "name", MaxNameLength);
        if (name is null)
        {
            error = Problem(AnswerTools.SpeciesRange, "'name' is required", """{"name": "Pinus kesiya", "country": "Thailand"}""");
            return false;
        }

        arguments = new SpeciesRangeArguments(name, ReadText(root, "country", MaxCountryLength));
        return true;
    }

    /// <summary>Regions one map call may name. More is a country coloured in.</summary>
    public const int MaxRegionsPerMap = 30;

    /// <summary>
    /// Reads a <c>region_map</c> call: a country and its regions, each as a name or as an object with a
    /// name and a share. A share written as a percentage (40) is read as a fraction (0.4).
    /// </summary>
    public static bool TryReadRegionMap(string? json, out RegionMapArguments arguments, out string error)
    {
        const string example = """{"country": "Vietnam", "regions": [{"name": "Lạng Sơn", "share": 0.7}, {"name": "Quảng Ninh", "share": 0.3}]}""";

        arguments = new RegionMapArguments(string.Empty, []);
        if (!TryReadObject(json, out var root, out error))
        {
            error = Problem(AnswerTools.RegionMap, error, example);
            return false;
        }

        var country = ReadText(root, "country", MaxCountryLength);
        if (country is null)
        {
            error = Problem(AnswerTools.RegionMap, "'country' is required", example);
            return false;
        }

        if (!TryGetProperty(root, "regions", out var regionsElement) || regionsElement.ValueKind != JsonValueKind.Array)
        {
            error = Problem(AnswerTools.RegionMap, "'regions' must be a list of region names, or of objects with a name and a share", example);
            return false;
        }

        var regions = new List<RegionShare>();
        foreach (var element in regionsElement.EnumerateArray())
        {
            if (regions.Count >= MaxRegionsPerMap)
            {
                break;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.String when !string.IsNullOrWhiteSpace(element.GetString()):
                    regions.Add(new RegionShare(Shorten(element.GetString()!.Trim(), MaxNameLength), 1d));
                    break;

                case JsonValueKind.Object:
                    {
                        var name = ReadText(element, "name", MaxNameLength) ?? ReadText(element, "region", MaxNameLength);
                        if (name is null)
                        {
                            continue;
                        }

                        var share = ReadFraction(element, "share") ?? ReadFraction(element, "weight") ?? ReadFraction(element, "frequency") ?? 1d;
                        if (share > 1d)
                        {
                            share /= 100d;
                        }

                        regions.Add(new RegionShare(name, Math.Clamp(share, 0d, 1d)));
                        break;
                    }
            }
        }

        if (regions.Count == 0)
        {
            error = Problem(AnswerTools.RegionMap, "'regions' names no region", example);
            return false;
        }

        arguments = new RegionMapArguments(country, regions);
        return true;
    }

    private static double? ReadFraction(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString()?.TrimEnd('%', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>The arguments in one line and at most <paramref name="maxLength"/> characters, for logs and summaries.</summary>
    public static string Shorten(string? json, int maxLength = 120)
    {
        var compact = Whitespace().Replace(json ?? string.Empty, " ").Trim();
        return compact.Length <= maxLength ? compact : compact[..(maxLength - 1)] + "…";
    }

    private static string Problem(string tool, string reason, string example) =>
        $"Invalid arguments for {tool}: {reason}. Call it like {example}.";

    /// <summary>
    /// The arguments as a JSON object. An object some models write as a JSON string is unwrapped once;
    /// empty arguments are an empty object, so the missing field is what gets named.
    /// </summary>
    private static bool TryReadObject(string? json, out JsonElement root, out string error)
    {
        root = default;
        error = string.Empty;

        var text = string.IsNullOrWhiteSpace(json) ? "{}" : json;
        try
        {
            using var document = JsonDocument.Parse(text);
            var element = document.RootElement;

            if (element.ValueKind == JsonValueKind.String && element.GetString() is { } inner)
            {
                using var unwrapped = JsonDocument.Parse(string.IsNullOrWhiteSpace(inner) ? "{}" : inner);
                element = unwrapped.RootElement.Clone();
            }
            else
            {
                element = element.Clone();
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                error = "the arguments must be a JSON object";
                return false;
            }

            root = element;
            return true;
        }
        catch (JsonException)
        {
            error = "the arguments are not valid JSON";
            return false;
        }
    }

    /// <summary>A text field, whitespace collapsed and cut to <paramref name="maxLength"/>, or null when absent or empty.</summary>
    private static string? ReadText(JsonElement root, string name, int maxLength)
    {
        if (!TryGetProperty(root, name, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var collapsed = Whitespace().Replace(text, " ").Trim();
        return collapsed.Length <= maxLength ? collapsed : collapsed[..maxLength].TrimEnd();
    }

    /// <summary>True for <c>true</c> or <c>"true"</c>; everything else, absence included, is false.</summary>
    private static bool ReadFlag(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value)
        && (value.ValueKind == JsonValueKind.True
            || (value.ValueKind == JsonValueKind.String
                && bool.TryParse(value.GetString()?.Trim(), out var flag) && flag));

    /// <summary>A whole number, written as one or as a string holding one ("3", "[3]").</summary>
    private static int? ReadNumber(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetInt32(out var whole) ? whole
                : value.TryGetDouble(out var real) && real % 1 == 0 && real is >= int.MinValue and <= int.MaxValue ? (int)real
                : null;
        }

        if (value.ValueKind == JsonValueKind.String
            && FirstNumber().Match(value.GetString() ?? string.Empty) is { Success: true } match
            && int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>Property names compared without regard to case: "Query" is meant as "query".</summary>
    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            }
        }

        value = default;
        return false;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex("[0-9]{1,6}", RegexOptions.CultureInvariant)]
    private static partial Regex FirstNumber();
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Tools;

/// <param name="Query">What to search for, trimmed to <see cref="ToolArguments.MaxQueryLength"/>.</param>
/// <param name="Country">The country to prefer, as the model wrote it, or null.</param>
public sealed record SearchGuidesArguments(string Query, string? Country, bool PicturesOnly);

/// <param name="Excerpt">The marker of the excerpt whose guide to read.</param>
public sealed record ReadGuideArguments(int Excerpt);

/// <param name="Name">The species' scientific or common name, trimmed to <see cref="ToolArguments.MaxNameLength"/>.</param>
/// <param name="Country">The country whose regions to count, as the model wrote it, or null.</param>
public sealed record SpeciesRangeArguments(string Name, string? Country);

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
            ReadFlag(root, "pictures_only"));
        return true;
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

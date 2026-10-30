using System.Globalization;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Which rated answers of an export <c>answer</c> replays, from <c>--only</c>: record numbers and ranges
/// ("1-25,31", as the report numbers them: the record's line in the export), feedback-id prefixes
/// ("0b7e0f8e"), and dates ("after:2026-09-27", "before:2026-10-01", by the day the answer was rated, UTC).
///
/// Numbers and ids pick records, any of them; dates narrow whatever is picked, or the whole export when
/// nothing is. So "after:2026-09-27" is every answer rated since the first export was taken.
/// </summary>
public sealed record RecordFilter(
    IReadOnlyList<(int From, int To)> Numbers,
    IReadOnlyList<string> IdPrefixes,
    DateOnly? After,
    DateOnly? Before)
{
    /// <summary>Shorter prefixes would be numbers, or match half the export.</summary>
    private const int MinIdPrefixLength = 4;

    /// <summary>The filter <paramref name="text"/> describes, or null when any part of it cannot be read.</summary>
    public static RecordFilter? Parse(string text)
    {
        var numbers = new List<(int, int)>();
        var ids = new List<string>();
        DateOnly? after = null, before = null;

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        foreach (var part in parts)
        {
            if (TryDate(part, "after:", out var date))
            {
                after = date;
            }
            else if (TryDate(part, "before:", out date))
            {
                before = date;
            }
            else if (TryRange(part, out var range))
            {
                numbers.Add(range);
            }
            else if (part.Replace("-", string.Empty, StringComparison.Ordinal) is { Length: >= MinIdPrefixLength } id
                     && id.All(Uri.IsHexDigit))
            {
                ids.Add(id.ToLowerInvariant());
            }
            else
            {
                return null;
            }
        }

        return new RecordFilter(numbers, ids, after, before);
    }

    public bool Matches(RatedQuestion question)
    {
        var record = question.Record;
        var picked = (Numbers.Count == 0 && IdPrefixes.Count == 0)
                     || Numbers.Any(range => question.Number >= range.From && question.Number <= range.To)
                     || IdPrefixes.Any(prefix => record.FeedbackId.ToString("N").StartsWith(prefix, StringComparison.Ordinal));

        var day = DateOnly.FromDateTime(record.CreatedAtUtc.UtcDateTime);
        return picked
               && (After is not { } after || day > after)
               && (Before is not { } before || day < before);
    }

    private static bool TryDate(string part, string prefix, out DateOnly date)
    {
        date = default;
        return part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && DateOnly.TryParseExact(part[prefix.Length..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryRange(string part, out (int From, int To) range)
    {
        range = default;
        var bounds = part.Split('-');
        if (bounds.Length is < 1 or > 2
            || !int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
            || from <= 0)
        {
            return false;
        }

        var to = from;
        if (bounds.Length == 2 && (!int.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out to) || to < from))
        {
            return false;
        }

        // A long run of digits is a feedback-id prefix, not a record number.
        if (bounds[0].Length > 5)
        {
            return false;
        }

        range = (from, to);
        return true;
    }
}

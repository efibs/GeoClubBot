using System.Text;
using System.Text.Json;

namespace GeoClubBot.ApiProbe;

/// <summary>
/// Summarises a JSON array of objects: which fields exist, how often, and what values they take.
///
/// This is the reason the probe exists. The solution's typed DTOs only declare the fields the bot
/// already knows about, so a new discriminator on an endpoint is invisible through them. The
/// census walks the raw payload instead, so an unexpected field shows up the first time it is
/// returned.
/// </summary>
public static class FieldCensus
{
    /// <summary>Distinct values listed per field before collapsing to a count.</summary>
    private const int MaxDistinctValuesListed = 25;

    /// <summary>A field with at most this many distinct values is treated as categorical.</summary>
    private const int CategoricalThreshold = 25;

    public static string Build(IReadOnlyList<JsonElement> items, string? crossTabAgainst = null)
    {
        if (items.Count == 0)
        {
            return "Field census: no items to inspect.";
        }

        var fields = new Dictionary<string, FieldStats>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            Walk(item, prefix: string.Empty, fields);
        }

        var report = new StringBuilder();
        report.AppendLine($"Field census over {items.Count} item(s)");
        report.AppendLine(new string('-', 72));

        foreach (var (path, stats) in fields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var presence = stats.PresentCount == items.Count
                ? "always"
                : $"{stats.PresentCount}/{items.Count}";

            report.AppendLine($"{path}  [{string.Join('|', stats.Kinds.OrderBy(k => k))}]  present: {presence}");

            if (stats.DistinctValues.Count <= MaxDistinctValuesListed)
            {
                foreach (var (value, count) in stats.DistinctValues.OrderByDescending(v => v.Value).ThenBy(v => v.Key, StringComparer.Ordinal))
                {
                    report.AppendLine($"    {count,6} x  {value}");
                }
            }
            else
            {
                var sample = stats.DistinctValues.Take(5).Select(v => v.Key);
                report.AppendLine($"    {stats.DistinctValues.Count} distinct values, e.g. {string.Join(", ", sample)}");
            }
        }

        if (crossTabAgainst is not null && fields.ContainsKey(crossTabAgainst))
        {
            AppendCrossTabs(report, items, fields, crossTabAgainst);
        }

        return report.ToString();
    }

    /// <summary>
    /// Every categorical field crossed against <paramref name="measureField"/>. For the activities
    /// feed this is what answers "do a daily mission and a daily challenge at 20 XP look different?"
    /// in one glance.
    /// </summary>
    private static void AppendCrossTabs(
        StringBuilder report,
        IReadOnlyList<JsonElement> items,
        Dictionary<string, FieldStats> fields,
        string measureField)
    {
        var categorical = fields
            .Where(f => f.Key != measureField
                        && f.Value.DistinctValues.Count > 1
                        && f.Value.DistinctValues.Count <= CategoricalThreshold
                        // A field with a distinct value per item is an id or a timestamp, not a
                        // category - cross-tabulating it just prints the feed back at you.
                        && f.Value.DistinctValues.Count < items.Count)
            .Select(f => f.Key)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (categorical.Count == 0)
        {
            report.AppendLine();
            report.AppendLine($"No categorical field to cross-tabulate against '{measureField}'.");
            return;
        }

        foreach (var path in categorical)
        {
            report.AppendLine();
            report.AppendLine($"{path}  x  {measureField}");
            report.AppendLine(new string('-', 72));

            var pairs = new Dictionary<(string Category, string Measure), int>();
            foreach (var item in items)
            {
                var category = Render(Find(item, path));
                var measure = Render(Find(item, measureField));
                var key = (category, measure);
                pairs[key] = pairs.GetValueOrDefault(key) + 1;
            }

            foreach (var ((category, measure), count) in pairs
                         .OrderBy(p => p.Key.Category, StringComparer.Ordinal)
                         .ThenBy(p => p.Key.Measure, StringComparer.Ordinal))
            {
                report.AppendLine($"    {count,6} x  {category}  ->  {measure}");
            }
        }
    }

    /// <summary>
    /// Records every leaf path of <paramref name="element"/>. Nested objects are flattened with
    /// dotted paths; arrays are recorded by length only, since their contents vary per item.
    /// </summary>
    private static void Walk(JsonElement element, string prefix, Dictionary<string, FieldStats> fields)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            Record(fields, prefix.Length == 0 ? "<value>" : prefix, element);
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Walk(property.Value, path, fields);
            }
            else
            {
                Record(fields, path, property.Value);
            }
        }
    }

    private static void Record(Dictionary<string, FieldStats> fields, string path, JsonElement value)
    {
        if (!fields.TryGetValue(path, out var stats))
        {
            stats = new FieldStats();
            fields[path] = stats;
        }

        stats.PresentCount++;
        stats.Kinds.Add(value.ValueKind.ToString());

        var rendered = Render(value);
        stats.DistinctValues[rendered] = stats.DistinctValues.GetValueOrDefault(rendered) + 1;
    }

    private static JsonElement? Find(JsonElement item, string path)
    {
        var current = item;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static string Render(JsonElement? element) => element switch
    {
        null => "<absent>",
        { ValueKind: JsonValueKind.Null } => "null",
        { ValueKind: JsonValueKind.String } value => $"\"{value.GetString()}\"",
        { ValueKind: JsonValueKind.Array } value => $"<array[{value.GetArrayLength()}]>",
        { ValueKind: JsonValueKind.Object } value => $"<object[{value.EnumerateObject().Count()}]>",
        { } value => value.ToString()
    };

    private sealed class FieldStats
    {
        public int PresentCount { get; set; }

        public HashSet<string> Kinds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> DistinctValues { get; } = new(StringComparer.Ordinal);
    }
}

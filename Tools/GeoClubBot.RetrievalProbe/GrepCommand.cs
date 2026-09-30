using System.Text.RegularExpressions;
using Google.Protobuf.Collections;
using Qdrant.Client.Grpc;
using Match = System.Text.RegularExpressions.Match;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Every indexed chunk whose text matches a pattern. Answers the question a replay cannot: was the
/// answer in the index at all? If it was and retrieval did not offer it, the fault is retrieval; if it
/// was not, it is the library, and no retrieval change will help.
/// </summary>
public static class GrepCommand
{
    private const uint PageSize = 512;

    /// <summary>Characters of context shown either side of a match.</summary>
    private const int Context = 70;

    public static async Task RunAsync(ProbeContext context)
    {
        if (context.Arguments.Target is not { Length: > 0 } pattern)
        {
            throw new ProbeException("grep needs a pattern, e.g. grep \"khasi|kesiya\"");
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new ProbeException($"'{pattern}' is not a valid pattern: {ex.Message}");
        }

        var fields = new WithPayloadSelector
        {
            Include = new PayloadIncludeSelector { Fields = { "text", "sourceUrl", "chunkKind", "country", "sectionPath" } }
        };

        var matches = new List<(string Country, string Section, string Url, bool IsPicture, string Excerpt)>();
        var scanned = 0;
        PointId? offset = null;

        do
        {
            var page = await context.Qdrant.ScrollAsync(
                context.Settings.Collection,
                limit: PageSize,
                offset: offset,
                payloadSelector: fields,
                vectorsSelector: false,
                cancellationToken: context.CancellationToken).ConfigureAwait(false);

            foreach (var point in page.Result)
            {
                scanned++;
                var text = Read(point.Payload, "text");
                var match = regex.Match(text);
                if (match.Success)
                {
                    matches.Add((Read(point.Payload, "country"), Read(point.Payload, "sectionPath"),
                        Read(point.Payload, "sourceUrl"), Read(point.Payload, "chunkKind") == "image",
                        Around(text, match)));
                }
            }

            offset = page.NextPageOffset;
        }
        while (offset is not null);

        var report = context.Report;
        report.Line($"# Chunks matching /{pattern}/");
        report.Line();
        report.Line($"{matches.Count} of {scanned} chunks, from "
                    + $"{matches.Select(match => match.Url.Split('#')[0]).Distinct(StringComparer.Ordinal).Count()} guide(s).");

        if (matches.Count == 0)
        {
            return;
        }

        report.Line();
        report.Line("By country: " + string.Join(", ", matches
            .GroupBy(match => match.Country.Length > 0 ? match.Country : "(none)")
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key} {group.Count()}")));
        report.Line();

        foreach (var match in matches.Take(context.Arguments.Max))
        {
            var label = HitDescriptions.Cell(match.Section.Length > 0 ? match.Section : match.Url, 60)
                .Replace("[", "(").Replace("]", ")");
            report.Line($"- {(match.IsPicture ? "🖼 " : string.Empty)}[{label}]({match.Url}) — {match.Excerpt}");
        }

        if (matches.Count > context.Arguments.Max)
        {
            report.Line();
            report.Line($"_{matches.Count - context.Arguments.Max} more; raise --max to see them._");
        }
    }

    private static string Around(string text, Match match)
    {
        var start = Math.Max(0, match.Index - Context);
        var end = Math.Min(text.Length, match.Index + match.Length + Context);
        var excerpt = HitDescriptions.Cell(text[start..end], 2 * Context + match.Length + 10);

        return $"{(start > 0 ? "…" : string.Empty)}{excerpt}{(end < text.Length ? "…" : string.Empty)}";
    }

    private static string Read(MapField<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var value) ? value.StringValue ?? string.Empty : string.Empty;
}

using System.Globalization;
using System.Text.RegularExpressions;
using Qdrant.Client.Grpc;
using UseCases.OutputPorts.AI;
using Match = Qdrant.Client.Grpc.Match;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Chunks in full, as the index holds them. Two questions a replay leaves open:
/// <list type="bullet">
/// <item><b>What was the rated answer shown?</b> A feedback export keeps the ids of the chunks behind
/// every excerpt an answer was offered and cited, not their text. Given an export, each rated answer's
/// excerpts are read back by id and printed whole, numbered as the model saw them, the cited ones
/// marked — so "the guide says more than the answer" and "the picture was never offered" can be told
/// apart by reading, not guessing.</item>
/// <item><b>What does the index hold of one guide?</b> Given <c>--source</c>, every chunk of that guide
/// in reading order: its text, its section, and whether it carries a picture. This is where a graphic
/// that "is in the doc" but was never offered shows up as a picture filed under another section's text,
/// or as no chunk at all.</item>
/// </list>
/// Reads by id and by scroll only; nothing is embedded, so it needs no key.
/// </summary>
public static partial class ChunksCommand
{
    /// <summary>A guide's chunks read back at most. The largest hold a few hundred.</summary>
    private const int MaxGuideChunks = 5_000;

    public static Task RunAsync(ProbeContext context) =>
        context.Arguments.Source is { Length: > 0 } source
            ? GuideAsync(context, source)
            : RatedAsync(context);

    private static async Task RatedAsync(ProbeContext context)
    {
        var questions = context.ReadRatedQuestions()
            .Where(question => context.Arguments.Only?.Matches(question) != false)
            .ToList();

        if (questions.Count == 0)
        {
            throw new ProbeException("No rated answer in the export matched --only.");
        }

        var report = context.Report;
        report.Line("# What each rated answer was shown");

        foreach (var question in questions)
        {
            var answer = question.Record.Transcript
                .OrderBy(turn => turn.Ordinal)
                .LastOrDefault(turn => string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase));

            report.Line();
            report.Line($"## #{question.Number} {(question.IsNegative ? "👎" : "👍")} {HitDescriptions.Cell(question.Question, 160)}");
            if (!string.IsNullOrWhiteSpace(question.Record.Comment))
            {
                report.Line($"> {HitDescriptions.Cell(question.Record.Comment, 600)}");
            }

            var offeredIds = answer?.OfferedChunkIds ?? [];
            if (offeredIds.Count == 0)
            {
                report.Line();
                report.Line("_The export kept no chunk ids for this answer (rated before they were stored)._");
                continue;
            }

            var cited = (answer?.CitedChunkIds ?? []).ToHashSet();
            var hits = await context.Index.ReadChunksAsync(offeredIds, context.CancellationToken).ConfigureAwait(false);
            var byChunk = hits
                .SelectMany(hit => hit.ChunkIds.Select(id => (id, hit)))
                .GroupBy(pair => pair.id)
                .ToDictionary(group => group.Key, group => group.First().hit);

            // Numbered as offered: an id the index no longer holds was still an excerpt of its own — a
            // tool's result that never was in the index (a species range), or a guide re-indexed since.
            var shown = new HashSet<Guid>();
            var number = 0;
            foreach (var id in offeredIds)
            {
                if (!byChunk.TryGetValue(id, out var hit))
                {
                    number++;
                    report.Line();
                    report.Line($"### [{number}]{(cited.Contains(id) ? " ✓ cited" : string.Empty)} _not in the index now_ ({id})");
                    continue;
                }

                if (!shown.Add(hit.Id))
                {
                    continue;
                }

                number++;
                WriteChunk(report, hit, $"[{number}]{(hit.ChunkIds.Any(cited.Contains) ? " ✓ cited" : string.Empty)}");
            }
        }
    }

    private static async Task GuideAsync(ProbeContext context, string source)
    {
        var (sourceType, sourceKey) = await ResolveGuideAsync(context, source).ConfigureAwait(false);

        var chunks = (await context.Index
                .ReadSourceChunksAsync(sourceType, sourceKey, MaxGuideChunks, context.CancellationToken)
                .ConfigureAwait(false))
            .OrderBy(chunk => chunk.LocalKey ?? string.Empty, NaturalOrder.Instance)
            .ThenBy(chunk => chunk.Kind)
            .ToList();

        var report = context.Report;
        report.Line($"# Chunks of {sourceType}:{sourceKey}");
        report.Line();
        report.Line($"{chunks.Count} chunk(s): {chunks.Count(chunk => chunk.Kind == KnowledgeChunkKind.Image)} with a picture, "
                    + $"{chunks.Count(chunk => chunk.IsHeadingOnlyPicture)} of them with no text but a heading.");

        foreach (var chunk in chunks.Take(context.Arguments.Max))
        {
            WriteChunk(report, chunk, $"`{chunk.LocalKey}`");
        }

        if (chunks.Count > context.Arguments.Max)
        {
            report.Line();
            report.Line($"_{chunks.Count - context.Arguments.Max} more; raise --max to see them._");
        }
    }

    /// <summary>
    /// The guide <paramref name="source"/> names: <c>type:key</c> as <c>/ai sources</c> lists it, or a
    /// link as it stands under an answer. A link is matched exactly first; a PlonkIt tip's link names
    /// one tip, and a Google Doc's every chunk, so either leads to its guide.
    /// </summary>
    internal static async Task<(string SourceType, string SourceKey)> ResolveGuideAsync(ProbeContext context, string source)
    {
        if (!source.Contains("://", StringComparison.Ordinal))
        {
            var separator = source.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0 && separator < source.Length - 1)
            {
                return (source[..separator], source[(separator + 1)..]);
            }

            throw new ProbeException("--source takes a guide's link, or its type and key as type:key (e.g. plonkit:india).");
        }

        var fields = new WithPayloadSelector { Include = new PayloadIncludeSelector { Fields = { "sourceType", "sourceKey" } } };
        var exact = await context.Qdrant.ScrollAsync(
            context.Settings.Collection,
            filter: new Filter { Must = { new Condition { Field = new FieldCondition { Key = "sourceUrl", Match = new Match { Keyword = source } } } } },
            limit: 1,
            payloadSelector: fields,
            vectorsSelector: false,
            cancellationToken: context.CancellationToken).ConfigureAwait(false);

        var point = exact.Result.FirstOrDefault();
        if (point is null)
        {
            // Not as written: a link without its fragment or query, matched as a prefix by a full scan.
            var all = new WithPayloadSelector { Include = new PayloadIncludeSelector { Fields = { "sourceType", "sourceKey", "sourceUrl" } } };
            PointId? offset = null;
            do
            {
                var page = await context.Qdrant.ScrollAsync(
                    context.Settings.Collection,
                    limit: 512,
                    offset: offset,
                    payloadSelector: all,
                    vectorsSelector: false,
                    cancellationToken: context.CancellationToken).ConfigureAwait(false);

                point = page.Result.FirstOrDefault(candidate =>
                    candidate.Payload.TryGetValue("sourceUrl", out var url)
                    && url.StringValue.StartsWith(source, StringComparison.OrdinalIgnoreCase));
                offset = page.NextPageOffset;
            }
            while (point is null && offset is not null);
        }

        if (point is null)
        {
            throw new ProbeException($"No indexed chunk has a source link starting with {source}.");
        }

        return (point.Payload["sourceType"].StringValue, point.Payload["sourceKey"].StringValue);
    }

    private static void WriteChunk(ProbeReport report, KnowledgeHit hit, string lead)
    {
        var pictures = hit.ImageUrl is null ? 0 : 1 + hit.ExtraImageUrls.Count;
        var kind = hit.Kind == KnowledgeChunkKind.Image
            ? pictures > 1 ? $"🖼 ×{pictures.ToString(CultureInfo.InvariantCulture)}" : "🖼"
            : "text";
        var heading = hit.IsHeadingOnlyPicture ? " _(heading only)_" : string.Empty;

        report.Line();
        report.Line($"### {lead} {kind}{heading} · {hit.Country ?? "(no country)"} · {HitDescriptions.Cell(hit.SectionPath ?? "(no section)", 140)}");
        report.Line($"{hit.SourceType}:{hit.SourceKey}#{hit.LocalKey} · {HitDescriptions.Cell(hit.Title ?? "(no title)", 80)} · <{hit.SourceUrl}>");

        if (hit.ImageUrl is { } picture)
        {
            report.Line($"picture: {picture}");
            foreach (var extra in hit.ExtraImageUrls)
            {
                report.Line($"picture: {extra}");
            }
        }

        report.Line();
        foreach (var line in hit.Text.Split('\n'))
        {
            report.Line($"> {line.TrimEnd()}");
        }
    }

    /// <summary>
    /// Orders local keys the way a guide reads: runs of digits by their value, so "p9" comes before
    /// "p10", and "12-img-2" after "12-img-1".
    /// </summary>
    private sealed partial class NaturalOrder : IComparer<string>
    {
        public static NaturalOrder Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            var leftParts = Runs().Matches(left ?? string.Empty);
            var rightParts = Runs().Matches(right ?? string.Empty);

            for (var index = 0; index < Math.Min(leftParts.Count, rightParts.Count); index++)
            {
                var a = leftParts[index].Value;
                var b = rightParts[index].Value;

                var compared = char.IsAsciiDigit(a[0]) && char.IsAsciiDigit(b[0])
                    ? CompareNumbers(a, b)
                    : string.CompareOrdinal(a, b);

                if (compared != 0)
                {
                    return compared;
                }
            }

            return leftParts.Count.CompareTo(rightParts.Count);
        }

        private static int CompareNumbers(string left, string right)
        {
            var a = left.TrimStart('0');
            var b = right.TrimStart('0');
            return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
        }

        [GeneratedRegex("[0-9]+|[^0-9]+", RegexOptions.CultureInvariant)]
        private static partial Regex Runs();
    }
}

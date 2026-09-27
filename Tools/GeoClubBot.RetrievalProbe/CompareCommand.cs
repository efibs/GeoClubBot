using System.Text.Json;
using System.Text.RegularExpressions;
using Infrastructure.OutputAdapters.AI;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// The export's questions under other fusion weights and under approximate search, counted side by
/// side. This is the measurement the weights <see cref="QdrantKnowledgeIndex"/> ships with came from,
/// kept runnable so the next change to them is measured the same way rather than argued.
///
/// Every variant fuses the very lists the bot searches, re-weighted: only the weight differs.
/// </summary>
public static class CompareCommand
{
    private static readonly double[] PixelWeights = [1d, 0.5d, 0.25d, 0d];

    public static async Task RunAsync(ProbeContext context)
    {
        // A question asked twice would be counted twice.
        var questions = context.ReadRatedQuestions()
            .DistinctBy(question => question.Question, StringComparer.Ordinal)
            .ToList();

        var targets = ReadTargets(context.Arguments.TargetsPath);
        var limit = context.Arguments.Limit;
        var report = context.Report;

        var vectors = await context.Embeddings()
            .EmbedAsync([.. questions.Select(question => question.Question)], context.CancellationToken)
            .ConfigureAwait(false);

        string[] columns = ["shipped", .. PixelWeights.Select(weight => $"pixels ×{weight:0.##}"), "approximate"];
        var totals = new Counts[columns.Length];

        report.Line($"# Retrieval compared: {questions.Count} question(s), top {limit}");
        report.Line();
        report.Line("Each cell: " + (targets.Count > 0 ? "**on-target** · " : string.Empty)
                    + "**pixel-only** (found by its pixels alone) · **caption-only** (a picture whose only text is a "
                    + "guide heading). **shipped** is the bot as it runs; **pixels ×w** re-weights the "
                    + "question-against-pixels search; **approximate** skips exact search.");
        if (targets.Count == 0)
        {
            report.Line();
            report.Line("_Pass --targets to count on-target excerpts; see README.md._");
        }

        report.Line();
        report.Line($"| question | {string.Join(" | ", columns)} |");
        report.Line($"|---|{string.Concat(columns.Select(_ => "---|"))}");

        for (var index = 0; index < questions.Count; index++)
        {
            var query = new KnowledgeQuery { TextVector = vectors[index], Limit = limit };
            var exact = await context.Index
                .SearchListsAsync(query, exact: true, context.CancellationToken).ConfigureAwait(false);
            var approximate = await context.Index
                .SearchListsAsync(query, exact: false, context.CancellationToken).ConfigureAwait(false);

            List<(IReadOnlyList<WeightedHitList> Lists, IReadOnlyList<KnowledgeHit> Hits)> variants =
            [
                (exact, KnowledgeHitFusion.Fuse(exact, limit)),
                .. PixelWeights.Select(weight => Reweighted(exact, weight)).Select(lists => (lists, KnowledgeHitFusion.Fuse(lists, limit))),
                (approximate, KnowledgeHitFusion.Fuse(approximate, limit))
            ];

            var target = TargetFor(targets, questions[index].Question);
            var cells = new string[columns.Length];

            for (var column = 0; column < variants.Count; column++)
            {
                var (lists, hits) = variants[column];
                var counts = Count(lists, hits, target);
                totals[column] += counts;
                cells[column] = counts.Describe(target is not null);
            }

            report.Line($"| {HitDescriptions.Cell(questions[index].Question, 60)} | {string.Join(" | ", cells)} |");
        }

        report.Line($"| **total** | {string.Join(" | ", totals.Select(total => $"**{total.Describe(targets.Count > 0)}**"))} |");
    }

    private static IReadOnlyList<WeightedHitList> Reweighted(IReadOnlyList<WeightedHitList> lists, double pixelWeight) =>
        [.. lists.Select(list => list.Name == QdrantKnowledgeIndex.QuestionPixelSearch ? list with { Weight = pixelWeight } : list)];

    private static Counts Count(IReadOnlyList<WeightedHitList> lists, IReadOnlyList<KnowledgeHit> hits, Regex? target)
    {
        var byText = ReplayCommand.Ranks(lists, QdrantKnowledgeIndex.QuestionTextSearch);

        return new Counts(
            target is null ? 0 : hits.Count(hit => target.IsMatch(hit.Text)),
            hits.Count(hit => !byText.ContainsKey(hit.Id)),
            hits.Count(HitDescriptions.IsCaptionOnly));
    }

    /// <summary>The first target whose words appear in the question, as a pattern an on-target excerpt matches.</summary>
    private static Regex? TargetFor(IReadOnlyDictionary<string, string> targets, string question) =>
        targets.FirstOrDefault(target => question.Contains(target.Key, StringComparison.OrdinalIgnoreCase)) is { Key: not null } found
            ? new Regex(found.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            : null;

    private static Dictionary<string, string> ReadTargets(string? path)
    {
        if (path is null)
        {
            return [];
        }

        if (!File.Exists(path))
        {
            throw new ProbeException($"No targets file at '{path}'.");
        }

        try
        {
            var targets = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];

            // Checked up front, so a typo in the tenth pattern does not surface after nine questions.
            foreach (var pattern in targets.Values)
            {
                _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            }

            return targets;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new ProbeException(
                $"The targets file must be a JSON object of {{ \"words in the question\": \"regex\" }}: {ex.Message}");
        }
    }

    private readonly record struct Counts(int OnTarget, int PixelOnly, int CaptionOnly)
    {
        public static Counts operator +(Counts left, Counts right) =>
            new(left.OnTarget + right.OnTarget, left.PixelOnly + right.PixelOnly, left.CaptionOnly + right.CaptionOnly);

        public string Describe(bool withTarget) =>
            withTarget ? $"{OnTarget} · {PixelOnly} · {CaptionOnly}" : $"{PixelOnly} · {CaptionOnly}";
    }
}

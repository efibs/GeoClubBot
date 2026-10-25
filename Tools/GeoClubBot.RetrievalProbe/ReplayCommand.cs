using System.Globalization;
using Infrastructure.OutputAdapters.AI;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// For every rated answer in an export: what retrieval offers for its question today, where each
/// excerpt came from, and what has changed since the answer was rated.
///
/// The first thing to run on a new export. A bad answer has two causes that read identically in its
/// transcript — the right guide was never offered, or it was offered and ignored — and this is where
/// they come apart.
/// </summary>
public static class ReplayCommand
{
    public static async Task RunAsync(ProbeContext context)
    {
        var questions = context.ReadRatedQuestions();
        var report = context.Report;

        // The planner ships off, for the request it costs; --allow-chat replays as the bot searches with it on.
        var settings = context.Arguments.AllowChat
            ? ProbeRetrieval.Override(ProbeRetrieval.Shipped(), new Dictionary<string, string?> { ["QueryPlanner:Enabled"] = "true" })
            : ProbeRetrieval.Shipped();
        var retrieval = new ProbeRetrieval(context);
        var variants = new[] { new RetrievalVariant("shipped", settings) };
        var targets = ProbeTargets.Read(context.Arguments.TargetsPath);
        await retrieval.AskPlannerAsync(questions, variants).ConfigureAwait(false);
        var runs = await retrieval.RunAsync(questions, variants, context.Arguments.Limit, targets).ConfigureAwait(false);
        var limit = context.Arguments.Limit ?? settings.ExcerptLimit;

        report.Line($"# Retrieval replay: {questions.Count} rated answer(s)");
        report.Line();
        report.Line($"Collection `{context.Settings.Collection}`, {limit} excerpts per question, searched exactly as the bot searches today"
                    + (settings.QueryPlanner.Enabled
                        ? " with the query planner switched on (`AI:Retrieval:QueryPlanner:Enabled`), as --allow-chat asks."
                        : ", the query planner off as it ships (pass --allow-chat to replay with it on)."));
        report.Line();
        report.Line("**text** / **pixels**: the excerpt's rank when the lead wording is searched against chunk text, and "
                    + "against picture pixels (– = not in the top 40). ✓ the rated answer cited it · ★ not offered "
                    + "when the answer was rated · 🖼 a picture"
                    + (targets.Count > 0 ? " · ◎ on target (--targets)." : "."));

        var offeredTotal = 0;
        var pixelOnly = 0;
        var captionOnly = 0;
        var changed = 0;
        var onTargetTotal = 0;
        var targetedTotal = 0;

        for (var index = 0; index < questions.Count; index++)
        {
            var rated = questions[index];
            var record = rated.Record;
            var run = runs[index][0];
            var target = ProbeTargets.For(targets, rated.Question);

            var lists = run.Lists;
            var offered = run.Offered;
            var textRanks = Ranks(lists, QdrantKnowledgeIndex.QuestionTextSearch);
            var pixelRanks = Ranks(lists, QdrantKnowledgeIndex.QuestionPixelSearch);

            var offeredThen = record.RetrievedSourceUrls.ToHashSet(StringComparer.Ordinal);
            var cited = record.CitedSourceUrls.ToHashSet(StringComparer.Ordinal);
            var offeredNow = offered.Select(hit => hit.SourceUrl).ToHashSet(StringComparer.Ordinal);
            var gone = offeredThen.Except(offeredNow).ToList();
            var added = offeredNow.Except(offeredThen).Count();

            report.Line();
            report.Line($"## {(rated.IsNegative ? "👎" : "👍")} {HitDescriptions.Cell(rated.Question, 200)}");
            if (!string.IsNullOrWhiteSpace(record.Comment))
            {
                report.Line();
                report.Line($"> {HitDescriptions.Cell(record.Comment, 500)}");
            }

            report.Line();
            report.Line($"_Rated answer: depth {record.AnswerDepth}, {record.ModelId ?? "model unknown"}, "
                        + $"cited {cited.Count} of the {offeredThen.Count} guide(s) it was offered._");

            if (rated.QuestionHadScreenshot)
            {
                report.Line(run.Screenshot is { } standIn
                    ? $"_The question carried a screenshot. Its Discord link has most likely expired, so the indexed picture <{standIn}> "
                      + "stands in for it (--targets), left out of the results._"
                    : "_The question carried a screenshot. Its Discord link has most likely expired, so it is "
                      + "replayed by its text alone._");
            }

            report.Line(DescribeSearch(run));

            if (target is not null)
            {
                var onTarget = offered.Count(target.IsOnTarget);
                onTargetTotal += onTarget;
                targetedTotal += offered.Count;
                report.Line($"_On target: {onTarget} of {offered.Count}"
                            + (target.Countries.Count > 0
                                ? $", {offered.Count(target.IsInCountry)} from {string.Join("/", target.Countries)}"
                                : string.Empty)
                            + "._");
            }

            report.Line(gone.Count == 0 && added == 0
                ? "_Offered now: the same guides as when it was rated._"
                : $"_Offered now: {offeredThen.Count - gone.Count} of the guides offered then, and {added} new._");

            report.Line();
            report.Line("| # | text | pixels | excerpt | guide |");
            report.Line("|---|---|---|---|---|");

            for (var position = 0; position < offered.Count; position++)
            {
                var hit = offered[position];
                var textRank = textRanks.GetValueOrDefault(hit.Id);
                var marks = (cited.Contains(hit.SourceUrl) ? " ✓" : string.Empty)
                            + (offeredThen.Contains(hit.SourceUrl) ? string.Empty : " ★")
                            + (target?.IsOnTarget(hit) == true ? " ◎" : string.Empty);
                var picture = hit.Kind == KnowledgeChunkKind.Image ? "🖼 " : string.Empty;

                report.Line($"| {position + 1} | {Rank(textRank)} | {Rank(pixelRanks.GetValueOrDefault(hit.Id))} | "
                            + $"{picture}{HitDescriptions.Cell(hit.Text, 110)} | {HitDescriptions.GuideLink(hit)}{marks} |");

                pixelOnly += lists.Any(list => list.Name.Contains("→ text", StringComparison.Ordinal)
                                               && list.Hits.Any(other => other.Id == hit.Id)) ? 0 : 1;
                captionOnly += hit.IsHeadingOnlyPicture ? 1 : 0;
            }

            if (gone.Count > 0)
            {
                report.Line();
                report.Line($"No longer offered: {string.Join(", ", gone.Select(url => $"<{url}>"))}");
            }

            offeredTotal += offered.Count;
            changed += gone.Count > 0 || added > 0 ? 1 : 0;
        }

        report.Line();
        report.Line("## Summary");
        report.Line();
        report.Line($"- Excerpts found by their pixels alone: {pixelOnly} of {offeredTotal}");
        report.Line($"- Pictures whose only text is a guide heading: {captionOnly} of {offeredTotal}");
        report.Line($"- Answers offered different guides now than when they were rated: {changed} of {questions.Count}");
        if (targetedTotal > 0)
        {
            report.Line($"- On target: {onTargetTotal} of the {targetedTotal} excerpts offered to questions with a target");
        }
    }

    /// <summary>
    /// What the question was searched by: each wording and its weight, the countries boosted, the key
    /// phrases. The first thing to read when a follow-up was offered the wrong guides.
    /// </summary>
    private static string DescribeSearch(RetrievalRun run)
    {
        var plan = run.Plan;
        var wordings = new[] { plan.Lead }.Concat(plan.Others)
            .Select(wording => $"{wording.Name} «{HitDescriptions.Cell(wording.Text, 120)}»"
                               + (Math.Abs(wording.Weight - 1d) < 1e-9 ? string.Empty : $" ×{wording.Weight:0.##}"));

        return $"_Searched by {string.Join(", ", wordings)}"
               + (plan.Countries.Count > 0 ? $"; within {string.Join(", ", plan.Countries.Select(country => country.Name))}" : string.Empty)
               + (plan.KeyPhrases.Count > 0 ? $"; phrases {string.Join(", ", plan.KeyPhrases.Select(phrase => $"\"{phrase}\""))}" : string.Empty)
               + (run.Planned ? "; planned" : string.Empty)
               + "._";
    }

    /// <summary>1-based rank of every hit in the named list; a hit it does not hold has none.</summary>
    public static Dictionary<Guid, int> Ranks(IReadOnlyList<WeightedHitList> lists, string name) =>
        lists.FirstOrDefault(list => list.Name == name) is { } list
            ? list.Hits.Select((hit, index) => (hit.Id, Rank: index + 1)).ToDictionary(pair => pair.Id, pair => pair.Rank)
            : [];

    private static string Rank(int rank) => rank == 0 ? "–" : rank.ToString(CultureInfo.InvariantCulture);
}

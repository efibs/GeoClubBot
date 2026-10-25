using System.Globalization;
using Configuration;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// The export's questions under other retrieval settings, counted side by side: the bot as it was,
/// each change of the conversation-aware retrieval added in turn, and other excerpt counts. This is the
/// measurement the bot's retrieval defaults came from, kept runnable so the next change to them is
/// measured the same way rather than argued.
///
/// Every variant is the bot's own query building, search and fusion under different settings, so the
/// numbers are what users would get. A follow-up is replayed with its branch, as the bot replays it.
/// </summary>
public static class CompareCommand
{
    public static async Task RunAsync(ProbeContext context)
    {
        // A question asked twice in the same branch would be counted twice.
        var questions = context.ReadRatedQuestions()
            .DistinctBy(question => string.Join("\u001F", question.Context.Select(turn => turn.Content).Append(question.Question)),
                StringComparer.Ordinal)
            .ToList();

        var targets = ProbeTargets.Read(context.Arguments.TargetsPath);
        var variants = context.Arguments.VariantsPath is { } path
            ? ProbeRetrieval.ReadVariants(path)
            : BuiltInVariants();

        var retrieval = new ProbeRetrieval(context);
        await retrieval.AskPlannerAsync(questions, variants).ConfigureAwait(false);
        var runs = await retrieval.RunAsync(questions, variants, context.Arguments.Limit, targets).ConfigureAwait(false);

        var report = context.Report;
        var totals = new Counts[variants.Count];
        var hasCountries = targets.Any(target => target.Countries.Count > 0);

        report.Line($"# Retrieval compared: {questions.Count} question(s)");
        report.Line();
        report.Line("Each cell: " + (targets.Count > 0 ? "**on-target** excerpts" : "excerpts")
                    + (hasCountries ? " · **in the question's country**" : string.Empty)
                    + ". ↩ marks a follow-up, searched with its branch; 📷 a screenshot question searched by the"
                    + " indexed picture its target names in place of the expired screenshot (otherwise by its text alone)."
                    + (hasCountries ? " An excerpt is on target when it is about what was asked and, when the question names countries, from one of them." : string.Empty));
        if (targets.Count == 0)
        {
            report.Line();
            report.Line("_Pass --targets to count on-target excerpts; see README.md._");
        }

        if (context.Planner() is null && variants.Any(variant => variant.Settings.QueryPlanner.Enabled))
        {
            report.Line();
            report.Line("_The planner was not asked (pass --allow-chat), so variants using it search as the bot does when it fails._");
        }

        report.Line();
        report.Line("| variant | settings |");
        report.Line("|---|---|");
        foreach (var variant in variants)
        {
            report.Line($"| {variant.Name} | {Describe(variant, context.Arguments.Limit)} |");
        }

        report.Line();
        report.Line($"| question | {string.Join(" | ", variants.Select(variant => variant.Name))} |");
        report.Line($"|---|{string.Concat(variants.Select(_ => "---|"))}");

        for (var index = 0; index < questions.Count; index++)
        {
            var target = ProbeTargets.For(targets, questions[index].Question);
            var cells = new string[variants.Count];

            for (var column = 0; column < variants.Count; column++)
            {
                var counts = Count(runs[index][column], target);
                totals[column] += counts;
                cells[column] = counts.Describe(target is not null, target?.Countries.Count > 0);
            }

            var marker = (questions[index].IsFollowUp ? "↩ " : string.Empty) + (runs[index].FirstOrDefault()?.Screenshot is null ? string.Empty : "📷 ");
            report.Line($"| {marker}{HitDescriptions.Cell(questions[index].Question, 60)} | {string.Join(" | ", cells)} |");
        }

        // The share as well as the count: more excerpts always find more, and what matters is how much
        // of what the model reads is on topic.
        report.Line($"| **on-target** | {string.Join(" | ", totals.Select(total => $"**{total.OnTarget}** of {total.Offered} ({Share(total.OnTarget, total.Offered)})"))} |");
        if (hasCountries)
        {
            report.Line($"| **in the question's country** | {string.Join(" | ", totals.Select(total => $"{total.InCountry} of {total.CountryOffered} ({Share(total.InCountry, total.CountryOffered)})"))} |");
        }

        report.Line($"| **target excerpts offered** | {string.Join(" | ", totals.Select(total => $"**{total.ChunksOffered}** of {total.Chunks}"))} |");
        report.Line($"| pixel-only | {string.Join(" | ", totals.Select(total => total.PixelOnly.ToString(CultureInfo.InvariantCulture)))} |");
        report.Line($"| heading-only pictures | {string.Join(" | ", totals.Select(total => total.HeadingOnly.ToString(CultureInfo.InvariantCulture)))} |");
        report.Line($"| searched with a plan | {string.Join(" | ", totals.Select(total => total.Planned.ToString(CultureInfo.InvariantCulture)))} |");

        WriteTargetExcerpts(report, questions, targets, variants, runs);
        WritePlannerLatency(report, questions, retrieval, variants);
    }

    /// <summary>
    /// The cumulative steps of the conversation-aware retrieval at the old excerpt count, the planner on
    /// top of the shipped settings at other counts — it ships off, for the request it costs — and the
    /// shipped settings themselves, which are also what a question gets when the planner is on but
    /// fails, times out or sits out its pause.
    /// </summary>
    public static IReadOnlyList<RetrievalVariant> BuiltInVariants()
    {
        var shipped = ProbeRetrieval.Shipped();
        var before = ProbeRetrieval.Before();
        var context = ProbeRetrieval.Override(before, new Dictionary<string, string?> { ["ContextualQuery"] = "true" });
        var countries = ProbeRetrieval.Override(context, new Dictionary<string, string?> { ["CountryBoost:Enabled"] = "true" });
        var headings = ProbeRetrieval.Override(countries, new Dictionary<string, string?>
        {
            ["HeadingOnlyPictureWeight"] = shipped.HeadingOnlyPictureWeight.ToString(CultureInfo.InvariantCulture)
        });
        var planned = ProbeRetrieval.Override(shipped, new Dictionary<string, string?> { ["QueryPlanner:Enabled"] = "true" });

        return
        [
            new RetrievalVariant("before", before),
            new RetrievalVariant("+context", context),
            new RetrievalVariant("+countries", countries),
            new RetrievalVariant("+headings", headings),
            new RetrievalVariant("+planner", ProbeRetrieval.Override(planned, new Dictionary<string, string?>
            {
                ["ExcerptLimit"] = before.ExcerptLimit.ToString(CultureInfo.InvariantCulture)
            })),
            new RetrievalVariant($"+planner, {shipped.ExcerptLimit}", planned),
            new RetrievalVariant("+planner, 12", ProbeRetrieval.Override(planned, new Dictionary<string, string?> { ["ExcerptLimit"] = "12" })),
            new RetrievalVariant("shipped", shipped)
        ];
    }

    /// <summary>Where each named target excerpt was offered: its place, its place past the cut in brackets, or – when no search found it.</summary>
    private static void WriteTargetExcerpts(
        ProbeReport report,
        IReadOnlyList<RatedQuestion> questions,
        IReadOnlyList<QuestionTarget> targets,
        IReadOnlyList<RetrievalVariant> variants,
        RetrievalRun[][] runs)
    {
        var rows = new List<string>();
        for (var index = 0; index < questions.Count; index++)
        {
            if (ProbeTargets.For(targets, questions[index].Question) is not { Chunks.Count: > 0 } target)
            {
                continue;
            }

            foreach (var (label, pattern) in target.Chunks)
            {
                var cells = variants.Select((_, column) =>
                {
                    var run = runs[index][column];
                    var place = run.Ranked.ToList().FindIndex(hit => target.IsChunk(hit, pattern)) + 1;
                    return place == 0 ? "–" : place <= run.Offered.Count ? $"**{place}**" : $"({place})";
                });

                rows.Add($"| {HitDescriptions.Cell(questions[index].Question, 40)} | `{label}` | {string.Join(" | ", cells)} |");
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        report.Line();
        report.Line("## Target excerpts");
        report.Line();
        report.Line("Where each excerpt that answers a question was placed: **offered** at that place, (n) ranked past the cut, – found by no search.");
        report.Line();
        report.Line($"| question | excerpt | {string.Join(" | ", variants.Select(variant => variant.Name))} |");
        report.Line($"|---|---|{string.Concat(variants.Select(_ => "---|"))}");
        foreach (var row in rows)
        {
            report.Line(row);
        }
    }

    /// <summary>How long the planner took, against how long the bot waits for it.</summary>
    private static void WritePlannerLatency(
        ProbeReport report,
        IReadOnlyList<RatedQuestion> questions,
        ProbeRetrieval retrieval,
        IReadOnlyList<RetrievalVariant> variants)
    {
        var replies = questions.Select(retrieval.ReplyFor).OfType<PlannerReply>().ToList();
        if (replies.Count == 0)
        {
            return;
        }

        var answered = replies.Where(reply => reply.Reply is not null).ToList();
        var seconds = answered.Select(reply => reply.Seconds).Order().ToList();
        var timeout = variants.Select(variant => variant.Settings.QueryPlanner)
            .FirstOrDefault(planner => planner.Enabled)?.TimeoutSeconds ?? new AiQueryPlannerConfiguration().TimeoutSeconds;
        var maxPhrasings = variants.Select(variant => variant.Settings.QueryPlanner.MaxPhrasings).DefaultIfEmpty(3).Max();
        var readable = answered.Count(reply => UseCases.UseCases.AI.Retrieval.RetrievalQueryPlanner.Parse(reply.Reply, maxPhrasings) is not null);

        report.Line();
        report.Line("## Planner");
        report.Line();
        report.Line($"- Asked {replies.Count}, answered {answered.Count}, readable {readable}, "
                    + $"within the bot's {timeout} s: {answered.Count(reply => reply.Seconds <= timeout)}.");
        if (seconds.Count > 0)
        {
            report.Line($"- Seconds: median {seconds[seconds.Count / 2]:0.0}, slowest {seconds[^1]:0.0}. "
                        + $"Models: {string.Join(", ", answered.GroupBy(reply => reply.Model).Select(group => $"{group.Key} {group.Count()}"))}.");
        }
    }

    private static string Describe(RetrievalVariant variant, int? limitOverride)
    {
        var settings = variant.Settings;
        var parts = new List<string> { $"{limitOverride ?? settings.ExcerptLimit} excerpts" };
        if (settings.ContextualQuery)
        {
            parts.Add($"branch ({settings.ContextUserTurns} question(s), own words ×{settings.FollowUpQuestionWeight:0.##})");
        }

        if (settings.CountryBoost.Enabled)
        {
            parts.Add($"countries ×{settings.CountryBoost.Weight:0.##}, screenshot ×{settings.CountryBoost.ScreenshotWeightWhenNamed:0.##}");
        }

        if (settings.HeadingOnlyPictureWeight < 1)
        {
            parts.Add($"heading-only pictures ×{settings.HeadingOnlyPictureWeight:0.##}");
        }

        if (settings.QueryPlanner.Enabled)
        {
            parts.Add($"planner ({settings.QueryPlanner.MaxPhrasings} phrasings ×{settings.QueryPlanner.PhrasingWeight:0.##})");
        }

        if (settings.KeyPhraseWeight > 0)
        {
            parts.Add($"key phrases ×{settings.KeyPhraseWeight:0.##}");
        }

        if (variant.PixelWeight is { } pixels)
        {
            parts.Add($"question → pixels ×{pixels:0.##}");
        }

        if (variant.Approximate)
        {
            parts.Add("approximate search");
        }

        return string.Join(", ", parts);
    }

    private static Counts Count(RetrievalRun run, QuestionTarget? target)
    {
        // Found by a text search of some wording, filtered or not; anything else came from pixels alone.
        var byText = run.Lists
            .Where(list => list.Name.Contains("→ text", StringComparison.Ordinal))
            .SelectMany(list => list.Hits)
            .Select(hit => hit.Id)
            .ToHashSet();

        var offered = run.Offered;
        var countries = target?.Countries ?? [];

        return new Counts(
            offered.Count,
            target is null ? 0 : offered.Count(target.IsOnTarget),
            countries.Count == 0 ? 0 : offered.Count,
            countries.Count == 0 ? 0 : offered.Count(target!.IsInCountry),
            target?.Chunks.Count ?? 0,
            target?.Chunks.Count(chunk => offered.Any(hit => target.IsChunk(hit, chunk.Pattern))) ?? 0,
            offered.Count(hit => !byText.Contains(hit.Id)),
            offered.Count(hit => hit.IsHeadingOnlyPicture),
            run.Planned ? 1 : 0);
    }

    private static string Share(int part, int whole) =>
        whole == 0 ? "–" : (part / (double)whole).ToString("P0", CultureInfo.InvariantCulture);

    private readonly record struct Counts(
        int Offered,
        int OnTarget,
        int CountryOffered,
        int InCountry,
        int Chunks,
        int ChunksOffered,
        int PixelOnly,
        int HeadingOnly,
        int Planned)
    {
        public static Counts operator +(Counts left, Counts right) => new(
            left.Offered + right.Offered,
            left.OnTarget + right.OnTarget,
            left.CountryOffered + right.CountryOffered,
            left.InCountry + right.InCountry,
            left.Chunks + right.Chunks,
            left.ChunksOffered + right.ChunksOffered,
            left.PixelOnly + right.PixelOnly,
            left.HeadingOnly + right.HeadingOnly,
            left.Planned + right.Planned);

        public string Describe(bool withTarget, bool withCountry) =>
            !withTarget
                ? $"{Offered}"
                : withCountry
                    ? $"{OnTarget} · {InCountry}"
                    : $"{OnTarget}";
    }
}

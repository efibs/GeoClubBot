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
        var limit = context.Arguments.Limit;
        var report = context.Report;

        var vectors = await context.Embeddings()
            .EmbedAsync([.. questions.Select(question => question.Question)], context.CancellationToken)
            .ConfigureAwait(false);

        report.Line($"# Retrieval replay: {questions.Count} rated answer(s)");
        report.Line();
        report.Line($"Collection `{context.Settings.Collection}`, {limit} excerpts per question, searched exactly as the bot searches today.");
        report.Line();
        report.Line("**text** / **pixels**: the excerpt's rank when the question is searched against chunk text, and "
                    + "against picture pixels (– = not in the top 40). ✓ the rated answer cited it · ★ not offered "
                    + "when the answer was rated · 🖼 a picture.");

        var offeredTotal = 0;
        var pixelOnly = 0;
        var captionOnly = 0;
        var changed = 0;

        for (var index = 0; index < questions.Count; index++)
        {
            var rated = questions[index];
            var record = rated.Record;

            var lists = await context.Index
                .SearchListsAsync(new KnowledgeQuery { TextVector = vectors[index], Limit = limit },
                    cancellationToken: context.CancellationToken)
                .ConfigureAwait(false);

            var offered = KnowledgeHitFusion.Fuse(lists, limit);
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
                report.Line("_The question carried a screenshot. Its Discord link has most likely expired, so it is "
                            + "replayed by its text alone._");
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
                            + (offeredThen.Contains(hit.SourceUrl) ? string.Empty : " ★");
                var picture = hit.Kind == KnowledgeChunkKind.Image ? "🖼 " : string.Empty;

                report.Line($"| {position + 1} | {Rank(textRank)} | {Rank(pixelRanks.GetValueOrDefault(hit.Id))} | "
                            + $"{picture}{HitDescriptions.Cell(hit.Text, 110)} | {HitDescriptions.GuideLink(hit)}{marks} |");

                pixelOnly += textRank == 0 ? 1 : 0;
                captionOnly += HitDescriptions.IsCaptionOnly(hit) ? 1 : 0;
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
    }

    /// <summary>1-based rank of every hit in the named list; a hit it does not hold has none.</summary>
    public static Dictionary<Guid, int> Ranks(IReadOnlyList<WeightedHitList> lists, string name) =>
        lists.FirstOrDefault(list => list.Name == name) is { } list
            ? list.Hits.Select((hit, index) => (hit.Id, Rank: index + 1)).ToDictionary(pair => pair.Id, pair => pair.Rank)
            : [];

    private static string Rank(int rank) => rank == 0 ? "–" : rank.ToString(CultureInfo.InvariantCulture);
}

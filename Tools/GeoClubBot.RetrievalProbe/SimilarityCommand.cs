using System.Globalization;
using Google.Protobuf.Collections;
using Infrastructure.OutputAdapters.AI;
using Qdrant.Client.Grpc;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// How close a guide's chunks sit to a question, and how close a re-worded chunk would. For testing a
/// change to what gets embedded — a cleaner caption, a stripped link, a different header — against the
/// real index before re-indexing anything.
///
/// Variants are built with the ingestion's own <see cref="EmbeddingTextBuilder"/> from the chunk's
/// stored details, so they differ from the chunk in their text alone. Compare a variant with the
/// "rebuilt" column rather than "stored": both are embedded now and the same way, while the stored
/// vector may predate a change to the recipe.
/// </summary>
public static class SimilarityCommand
{
    /// <summary>How deep the question's own results are read to place a chunk or a variant.</summary>
    private const ulong RankDepth = 300;

    public static async Task RunAsync(ProbeContext context)
    {
        var question = context.Arguments.Question
                       ?? throw new ProbeException("similarity needs --question \"...\"");
        var source = context.Arguments.Source
                     ?? throw new ProbeException("similarity needs --source <url>, as listed under an answer or in a replay");
        var collection = context.Settings.Collection;
        var cancellationToken = context.CancellationToken;

        var chunks = (await context.Qdrant.ScrollAsync(
                collection,
                filter: new Filter { Must = { Keyword("sourceUrl", source) } },
                limit: 50,
                payloadSelector: true,
                vectorsSelector: false,
                cancellationToken: cancellationToken).ConfigureAwait(false))
            .Result;

        if (chunks.Count == 0)
        {
            throw new ProbeException($"No indexed chunk has the source URL {source}. Copy it from a replay or from under an answer.");
        }

        var rebuilt = chunks
            .Select(point => EmbeddingTextBuilder.Build(Descriptor(point.Payload), Chunk(point.Payload, Read(point.Payload, "text"))))
            .ToList();

        var variants = context.Arguments.Variants
            .Select(variant => EmbeddingTextBuilder.Build(Descriptor(chunks[0].Payload), Chunk(chunks[0].Payload, variant)))
            .ToList();

        var vectors = await context.Embeddings()
            .EmbedAsync([question, .. rebuilt, .. variants], cancellationToken)
            .ConfigureAwait(false);

        var ranked = await context.Qdrant.QueryAsync(
            collection,
            query: vectors[0],
            usingVector: QdrantKnowledgeIndex.TextVectorName,
            searchParams: new SearchParams { Exact = true },
            limit: RankDepth,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var scores = ranked.Select(point => point.Score).ToList();

        var report = context.Report;
        report.Line("# Similarity to a question");
        report.Line();
        report.Line($"> {question}");
        report.Line();
        report.Line($"Guide: <{source}>. The question's own results run from {scores.FirstOrDefault():0.0000} (1st) to "
                    + $"{scores.LastOrDefault():0.0000} ({scores.Count}th), searched against chunk text.");
        report.Line();
        report.Line("| chunk | stored | rank | rebuilt | text |");
        report.Line("|---|---|---|---|---|");

        for (var index = 0; index < chunks.Count; index++)
        {
            var point = chunks[index];
            var stored = (await context.Qdrant.QueryAsync(
                    collection,
                    query: vectors[0],
                    usingVector: QdrantKnowledgeIndex.TextVectorName,
                    filter: new Filter { Must = { new Condition { HasId = new HasIdCondition { HasId = { point.Id } } } } },
                    limit: 1,
                    cancellationToken: cancellationToken).ConfigureAwait(false))
                .FirstOrDefault()?.Score;

            var position = ranked.Select((hit, rank) => (hit.Id, rank)).FirstOrDefault(pair => pair.Id == point.Id);
            var rank = position.Id is not null
                ? (position.rank + 1).ToString(CultureInfo.InvariantCulture)
                : stored is { } score ? ProjectedRank(scores, score) : "–";

            report.Line($"| {index + 1} ({Read(point.Payload, "chunkKind")}) | {stored?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "–"} | {rank} | "
                        + $"{Cosine(vectors[0], vectors[1 + index]):0.0000} | {HitDescriptions.Cell(Read(point.Payload, "text"), 100)} |");
        }

        if (variants.Count == 0)
        {
            report.Line();
            report.Line("_Pass --variant \"re-worded text\" (repeatable) to see where a different wording would rank._");
            return;
        }

        report.Line();
        report.Line("| variant of chunk 1 | similarity | would rank | text |");
        report.Line("|---|---|---|---|");

        for (var index = 0; index < variants.Count; index++)
        {
            var similarity = Cosine(vectors[0], vectors[1 + chunks.Count + index]);
            report.Line($"| {index + 1} | {similarity:0.0000} | {ProjectedRank(scores, similarity)} | "
                        + $"{HitDescriptions.Cell(context.Arguments.Variants[index], 100)} |");
        }
    }

    /// <summary>Where a score would land among the question's results, the chunk itself aside.</summary>
    private static string ProjectedRank(IReadOnlyList<float> scores, double similarity)
    {
        var above = scores.Count(score => score > similarity);
        return above == scores.Count && scores.Count == (int)RankDepth
            ? $"> {RankDepth}"
            : $"~{(above + 1).ToString(CultureInfo.InvariantCulture)}";
    }

    private static SourceDescriptor Descriptor(MapField<string, Value> payload) =>
        new(Read(payload, "sourceType"),
            Read(payload, "sourceKey"),
            new Uri(Read(payload, "sourceUrl")),
            Title: NullIfEmpty(Read(payload, "title")),
            Country: NullIfEmpty(Read(payload, "country")));

    private static ExtractedChunk Chunk(MapField<string, Value> payload, string text) =>
        new(Read(payload, "localKey"), Read(payload, "sectionPath"), text);

    private static double Cosine(float[] left, float[] right)
    {
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    private static Condition Keyword(string field, string value) =>
        new() { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } } };

    private static string Read(MapField<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var value) ? value.StringValue ?? string.Empty : string.Empty;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}

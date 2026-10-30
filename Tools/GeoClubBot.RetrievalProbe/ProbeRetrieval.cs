using System.Globalization;
using System.Text.Json;
using Configuration;
using Infrastructure.OutputAdapters.AI;
using Microsoft.Extensions.Configuration;
using Qdrant.Client.Grpc;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Retrieval;

namespace GeoClubBot.RetrievalProbe;

/// <summary>What one question was searched by under one set of settings, and what came back.</summary>
/// <param name="Lists">Every list the index returned, as fused.</param>
/// <param name="Offered">What the model would be offered: the fused lists cut to the excerpt limit.</param>
/// <param name="Ranked">The same fusion uncut, to place a chunk that missed the cut.</param>
/// <param name="Planned">The planner's reading was used: it answered in time and could be read.</param>
/// <param name="Screenshot">The indexed picture searched in place of the question's expired screenshot, if any.</param>
public sealed record RetrievalRun(
    RetrievalSearchPlan Plan,
    IReadOnlyList<WeightedHitList> Lists,
    IReadOnlyList<KnowledgeHit> Offered,
    IReadOnlyList<KnowledgeHit> Ranked,
    bool Planned,
    string? Screenshot = null);

/// <summary>A named set of retrieval settings to replay the export under.</summary>
/// <param name="PixelWeight">
/// Re-weights the question-against-pixels list, which the bot weights by a constant
/// (<c>QdrantKnowledgeIndex.CrossModalWeight</c>) rather than by a setting. Null keeps it.
/// </param>
/// <param name="Approximate">Searches the approximate index instead of exactly, as the bot did before it searched exactly.</param>
public sealed record RetrievalVariant(
    string Name,
    AiRetrievalConfiguration Settings,
    double? PixelWeight = null,
    bool Approximate = false);

/// <summary>
/// Replays rated questions the way the bot retrieves for them: its <see cref="RetrievalQueryComposer"/>
/// builds the wordings from the question and its branch, its planner prompt and reader turn a cached
/// planner reply into a plan, its <see cref="QdrantKnowledgeIndex"/> searches and its fusion merges.
/// Only the planner's reply and the embeddings come from caches, so a rerun is free.
/// </summary>
public sealed class ProbeRetrieval(ProbeContext context)
{
    /// <summary>How deep the uncut fusion is kept, to place chunks that missed the cut.</summary>
    private const int RankDepth = 200;

    /// <summary>Planner requests in flight at once.</summary>
    private const int PlannerParallelism = 4;

    private readonly Dictionary<RatedQuestion, PlannerReply?> _replies = [];
    private readonly Dictionary<string, StandIn> _standIns = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _countries;

    /// <summary>The settings the bot ships with: the defaults <c>appsettings.json</c> repeats.</summary>
    public static AiRetrievalConfiguration Shipped() => new();

    /// <summary>
    /// The bot as it searched before conversation-aware retrieval: the question's own words, no
    /// country boost, no planner, every picture where it ranks, eight excerpts.
    /// </summary>
    public static AiRetrievalConfiguration Before() =>
        Override(Shipped(), new Dictionary<string, string?>
        {
            ["ExcerptLimit"] = "8",
            ["ContextualQuery"] = "false",
            ["QueryPlanner:Enabled"] = "false",
            ["CountryBoost:Enabled"] = "false",
            ["HeadingOnlyPictureWeight"] = "1",
            ["KeyPhraseWeight"] = "0"
        });

    /// <summary>A copy of <paramref name="settings"/> with the given configuration keys changed, as the bot would bind them.</summary>
    public static AiRetrievalConfiguration Override(AiRetrievalConfiguration settings, IReadOnlyDictionary<string, string?> values)
    {
        var copy = JsonSerializer.Deserialize<AiRetrievalConfiguration>(JsonSerializer.Serialize(settings))!;
        new ConfigurationBuilder().AddInMemoryCollection(values).Build().Bind(copy);
        return copy;
    }

    /// <summary>
    /// Variants from a JSON file: <c>{ "name": { "CountryBoost": { "Weight": 0.5 } } }</c>, each applied
    /// to the shipped settings, or to the ones before this change when it says <c>"base": "before"</c>.
    /// Two keys are the probe's own, for what the bot fixes in code rather than configuration:
    /// <c>"pixels": 0.5</c> re-weights the question-against-pixels list, and <c>"approximate": true</c>
    /// searches the approximate index.
    /// </summary>
    public static IReadOnlyList<RetrievalVariant> ReadVariants(string path)
    {
        if (!File.Exists(path))
        {
            throw new ProbeException($"No variants file at '{path}'.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            return [.. document.RootElement.EnumerateObject().Select(variant =>
            {
                var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                Flatten(variant.Value, prefix: null, values);
                var start = values.Remove("base", out var name) && name == "before" ? Before() : Shipped();
                double? pixels = values.Remove("pixels", out var weight) && weight is not null
                    ? double.Parse(weight, CultureInfo.InvariantCulture)
                    : null;
                var approximate = values.Remove("approximate", out var flag) && bool.TryParse(flag, out var parsed) && parsed;
                return new RetrievalVariant(variant.Name, Override(start, values), pixels, approximate);
            })];
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new ProbeException($"The variants file must be a JSON object of {{ \"name\": {{ settings }} }}: {ex.Message}");
        }
    }

    /// <summary>
    /// Asks the planner about every question that has a variant using it, when the probe may — from the
    /// cache where it can. Without <c>--allow-chat</c> nothing is asked, and those variants search
    /// without a plan, as the bot does when the planner fails.
    /// </summary>
    public async Task<int> AskPlannerAsync(IReadOnlyList<RatedQuestion> questions, IReadOnlyList<RetrievalVariant> variants)
    {
        if (!variants.Any(variant => variant.Settings.QueryPlanner.Enabled) || context.Planner() is not { } planner)
        {
            return 0;
        }

        // A few at a time: a free model takes half a minute to plan, and one at a time a whole export
        // takes longer than anyone waits. Few enough to stay well inside the provider's per-minute limit.
        var pending = questions.Where(question => !_replies.ContainsKey(question)).Distinct().ToList();
        var replies = new PlannerReply?[pending.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, pending.Count),
            new ParallelOptions { MaxDegreeOfParallelism = PlannerParallelism, CancellationToken = context.CancellationToken },
            async (index, cancellationToken) => replies[index] = await planner
                .AskAsync(pending[index].Context, pending[index].Question, pending[index].QuestionHadScreenshot, cancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);

        for (var index = 0; index < pending.Count; index++)
        {
            _replies[pending[index]] = replies[index];
        }

        return pending.Count;
    }

    /// <summary>The planner's reply to a question, if it was asked.</summary>
    public PlannerReply? ReplyFor(RatedQuestion question) => _replies.GetValueOrDefault(question);

    /// <summary>
    /// Searches every question under every variant. All wordings are embedded in one go, then each
    /// question and variant is one batch against the index.
    /// </summary>
    /// <param name="targets">
    /// Where a question's target names a <see cref="QuestionTarget.Screenshot"/> stand-in, a question
    /// that carried a screenshot is searched by that picture's pixels too; otherwise by its text alone.
    /// </param>
    /// <returns>Runs indexed by question, then by variant.</returns>
    public async Task<RetrievalRun[][]> RunAsync(
        IReadOnlyList<RatedQuestion> questions,
        IReadOnlyList<RetrievalVariant> variants,
        int? limitOverride = null,
        IReadOnlyList<QuestionTarget>? targets = null)
    {
        var plans = new RetrievalSearchPlan[questions.Count][];
        var planned = new bool[questions.Count][];

        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            plans[index] = new RetrievalSearchPlan[variants.Count];
            planned[index] = new bool[variants.Count];

            for (var column = 0; column < variants.Count; column++)
            {
                var settings = variants[column].Settings;
                var plan = PlanFor(question, settings);
                var gazetteer = settings.CountryBoost.Enabled
                    ? CountryGazetteer.Build(await CountriesAsync().ConfigureAwait(false), settings.CountryBoost.Aliases)
                    : CountryGazetteer.Empty;

                plans[index][column] = RetrievalQueryComposer.Compose(
                    question.Question, question.Context, question.QuestionHadScreenshot, plan, gazetteer, settings);
                planned[index][column] = plan is not null;
            }
        }

        var texts = plans.SelectMany(row => row).SelectMany(plan => plan.Texts).Distinct(StringComparer.Ordinal).ToList();
        var embedded = await context.Embeddings().EmbedAsync(texts, context.CancellationToken).ConfigureAwait(false);
        var vectors = texts.Select((text, index) => (text, index)).ToDictionary(pair => pair.text, pair => embedded[pair.index], StringComparer.Ordinal);

        var runs = new RetrievalRun[questions.Count][];
        for (var index = 0; index < questions.Count; index++)
        {
            var standIn = questions[index].QuestionHadScreenshot
                          && targets is not null
                          && ProbeTargets.For(targets, questions[index].Question) is { Screenshot: { } screenshot }
                ? await StandInAsync(screenshot).ConfigureAwait(false)
                : null;

            runs[index] = new RetrievalRun[variants.Count];
            for (var column = 0; column < variants.Count; column++)
            {
                var variant = variants[column];
                var plan = plans[index][column];
                var query = plan.ToQuery(
                    [.. plan.Texts.Select(text => (ReadOnlyMemory<float>)vectors[text])], standIn?.Vector, variant.Settings);
                var limit = limitOverride ?? query.Limit;

                var lists = await context.Index
                    .SearchListsAsync(query, exact: !variant.Approximate, context.CancellationToken)
                    .ConfigureAwait(false);

                if (variant.PixelWeight is { } pixelWeight)
                {
                    lists = [.. lists.Select(list => list.Name == QdrantKnowledgeIndex.QuestionPixelSearch
                        ? list with { Weight = query.TextWeight * pixelWeight }
                        : list)];
                }

                // The stand-in is in the index and the user's screenshot never was: it would find itself first.
                if (standIn is not null)
                {
                    lists = [.. lists.Select(list => list with { Hits = [.. list.Hits.Where(hit => hit.Id != standIn.Id)] })];
                }

                var ranked = KnowledgeHitFusion.Fuse(lists, RankDepth, query.HeadingOnlyPictureWeight);
                runs[index][column] = new RetrievalRun(
                    plan, lists, [.. ranked.Take(limit)], ranked, planned[index][column], standIn?.SourceUrl);
            }
        }

        return runs;
    }

    /// <summary>
    /// The stored pixels of the indexed picture with this source URL, searched in place of a
    /// screenshot whose Discord link has expired. A scroll, so it costs no embedding.
    /// </summary>
    private async Task<StandIn> StandInAsync(string sourceUrl)
    {
        if (_standIns.TryGetValue(sourceUrl, out var cached))
        {
            return cached;
        }

        var points = (await context.Qdrant.ScrollAsync(
                context.Settings.Collection,
                filter: new Filter
                {
                    Must =
                    {
                        new Condition { Field = new FieldCondition { Key = "sourceUrl", Match = new Match { Keyword = sourceUrl } } },
                        new Condition { HasVector = new HasVectorCondition { HasVector = QdrantKnowledgeIndex.ImageVectorName } }
                    }
                },
                limit: 1,
                payloadSelector: false,
                vectorsSelector: new WithVectorsSelector
                {
                    Include = new VectorsSelector { Names = { QdrantKnowledgeIndex.ImageVectorName } }
                },
                cancellationToken: context.CancellationToken).ConfigureAwait(false))
            .Result;

        var vector = points.Count == 0
            ? null
            : points[0].Vectors?.Vectors?.Vectors.GetValueOrDefault(QdrantKnowledgeIndex.ImageVectorName)?.GetDenseVector()?.Data;

        if (vector is null || vector.Count == 0)
        {
            throw new ProbeException(
                $"No indexed picture with pixels has the source URL {sourceUrl}, so it cannot stand in for a screenshot. "
                + "Copy the URL of a picture (🖼) from a replay.");
        }

        return _standIns[sourceUrl] = new StandIn(Guid.Parse(points[0].Id.Uuid), sourceUrl, [.. vector]);
    }

    /// <param name="Id">The picture's own point, left out of every result list.</param>
    private sealed record StandIn(Guid Id, string SourceUrl, float[] Vector);

    /// <summary>
    /// The plan the bot would have searched by: none when the planner is off, was not asked, failed,
    /// took longer than the bot waits, or answered with something unreadable.
    /// </summary>
    private RetrievalPlan? PlanFor(RatedQuestion question, AiRetrievalConfiguration settings) =>
        settings.QueryPlanner.Enabled
        && _replies.GetValueOrDefault(question) is { Reply: { } reply, Seconds: var seconds }
        && seconds <= settings.QueryPlanner.TimeoutSeconds
            ? RetrievalQueryPlanner.Parse(reply, settings.QueryPlanner.MaxPhrasings)
            : null;

    /// <summary>The index's own country names, read once: a scroll of every point, as the bot's directory reads them.</summary>
    private async Task<IReadOnlyList<string>> CountriesAsync() =>
        _countries ??= await context.Index.ListCountriesAsync(context.CancellationToken).ConfigureAwait(false);

    private static void Flatten(JsonElement element, string? prefix, Dictionary<string, string?> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, prefix is null ? property.Name : $"{prefix}:{property.Name}", values);
                }

                break;

            case JsonValueKind.Array:
                var position = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, $"{prefix}:{position++}", values);
                }

                break;

            default:
                values[prefix ?? string.Empty] = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
                break;
        }
    }
}

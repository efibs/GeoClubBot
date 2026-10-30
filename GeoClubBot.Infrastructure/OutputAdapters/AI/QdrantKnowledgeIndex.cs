using Qdrant.Client;
using Qdrant.Client.Grpc;
using UseCases.OutputPorts.AI;
using Match = Qdrant.Client.Grpc.Match;

namespace Infrastructure.OutputAdapters.AI;

/// <summary>
/// Qdrant-backed <see cref="IKnowledgeIndex"/>.
///
/// Each point carries up to two named vectors of the same width: <c>text</c> for the chunk's prose or
/// an image's caption, and <c>image</c> for the pixels. They are searched separately and merged with
/// reciprocal-rank fusion (<see cref="KnowledgeHitFusion"/>), which compares positions rather than
/// scores — necessary because text-to-text similarity sits on a visibly higher scale than
/// text-to-image, so a single blended search would rank every paragraph above every image regardless
/// of relevance.
/// </summary>
public sealed class QdrantKnowledgeIndex(QdrantClient client, string collectionName, int vectorSize)
    : IKnowledgeIndex
{
    public const string TextVectorName = "text";
    public const string ImageVectorName = "image";

    /// <summary>The written question against chunk text and captions — the <see cref="WeightedHitList.Name"/>.</summary>
    public const string QuestionTextSearch = "question → text";

    /// <summary>The written question against image pixels.</summary>
    public const string QuestionPixelSearch = "question → pixels";

    /// <summary>An attached screenshot against image pixels.</summary>
    public const string ScreenshotPixelSearch = "screenshot → pixels";

    /// <summary>The written question against chunk text, within the countries it is about.</summary>
    public const string CountryTextSearch = "question → text [countries]";

    /// <summary>An attached screenshot against image pixels, within the countries the question names.</summary>
    public const string CountryScreenshotSearch = "screenshot → pixels [countries]";

    /// <summary>The written question against the chunks containing one of its key phrases.</summary>
    public const string KeyPhraseSearch = "question → text [phrases]";

    private const uint ScrollPageSize = 256;

    /// <summary>Candidates drawn from each search before fusion; wider than the final limit so fusion has room to reorder.</summary>
    private const ulong PrefetchLimit = 40;

    /// <summary>
    /// How much a place in the question-against-pixels list counts, against the question-against-text
    /// list. Replayed on the production index with the beta testers' questions: at full weight, pictures
    /// found by their pixels alone took 53 of 144 top-8 slots. Their captions have nothing to do with
    /// the question — or they would have ranked by text — so the model can say nothing about them, and
    /// they had pushed out the very guides testers found missing: two Vietnam chunks on Khasi pines lost
    /// to three pictures of Ugandan forest. At a quarter such pictures take 13 of 144 slots.
    ///
    /// Not zero, because agreement still counts: "white car long antenna" ranks only 39th by its text
    /// but is first by its picture, and between them that is the answer.
    /// </summary>
    private const double CrossModalWeight = 0.25;

    public async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        var collections = await client.ListCollectionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (collections.Any(name => name == collectionName))
        {
            return;
        }

        await client.CreateCollectionAsync(
            collectionName: collectionName,
            vectorsConfig: new VectorParamsMap
            {
                Map =
                {
                    [TextVectorName] = new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine },
                    [ImageVectorName] = new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine }
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Filtered search without an index degrades to a full scan of the collection. The previous
        // implementation filtered on country with no index at all.
        foreach (var field in (string[])["country", "sourceType", "sourceKey", "chunkKind", "ingestRun"])
        {
            await client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: field,
                schemaType: PayloadSchemaType.Keyword,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public Task UpsertAsync(
        IReadOnlyList<KnowledgePoint> points,
        string ingestRun,
        CancellationToken cancellationToken = default)
    {
        if (points.Count == 0)
        {
            return Task.CompletedTask;
        }

        var structs = points.Select(point => ToPointStruct(point, ingestRun)).ToList();
        return client.UpsertAsync(collectionName, structs, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken = default) =>
        KnowledgeHitFusion.Fuse(
            await SearchListsAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false),
            query.Limit,
            query.HeadingOnlyPictureWeight);

    /// <summary>
    /// The per-vector result lists <see cref="SearchAsync"/> fuses, each carrying the weight it counts
    /// for. Public for <c>Tools/GeoClubBot.RetrievalProbe</c>, which replays questions through exactly
    /// these lists — and re-weights them, or searches approximately — to measure a change to
    /// retrieval against the real index before making it.
    ///
    /// Every list goes out in one batch, so a question costs the index one round trip however many
    /// wordings, countries and phrases it is searched by.
    /// </summary>
    /// <param name="exact">False searches the approximate index instead, for comparison only.</param>
    public async Task<IReadOnlyList<WeightedHitList>> SearchListsAsync(
        KnowledgeQuery query,
        bool exact = true,
        CancellationToken cancellationToken = default)
    {
        var filter = BuildFilter(query.Country, query.SourceType, ingestRun: null, sourceKey: null, query.ChunkKind);
        var searches = new List<(QueryPoints Search, double Weight, string Name)>();

        // The boosts narrow the hard filter rather than replace it, so /ai search's country still holds.
        var countryFilter = query.BoostCountries.Count > 0 ? WithCountries(filter, query.BoostCountries) : null;
        var phraseFilter = WithPhrases(filter, query.KeyPhrases);

        // Length is checked as well as presence: an empty vector is never a meaningful query, and
        // sending one fails the entire request rather than just that search.
        if (query.TextVector is { Length: > 0 } textVector)
        {
            // The question against chunk text and image captions — the strongest signal by a wide
            // margin, since captions and questions are both text.
            searches.Add((BuildSearch(textVector, TextVectorName, filter, exact), query.TextWeight, QuestionTextSearch));

            // The same question against image pixels, down-weighted: see CrossModalWeight.
            searches.Add((BuildSearch(textVector, ImageVectorName, filter, exact),
                query.TextWeight * CrossModalWeight, QuestionPixelSearch));

            // The same question again within the countries it is about. A chunk found both ways adds
            // up, so the named country's guides rise without anything else being shut out.
            if (countryFilter is not null)
            {
                searches.Add((BuildSearch(textVector, TextVectorName, countryFilter, exact),
                    query.TextWeight * query.CountryBoostWeight, CountryTextSearch));
            }

            // Chunks containing a phrase the question turns on, ranked by the question. A literal
            // "no supports" can sit far down a similarity ranking that the rest of its chunk sets.
            if (phraseFilter is not null)
            {
                searches.Add((BuildSearch(textVector, TextVectorName, phraseFilter, exact),
                    query.KeyPhraseWeight, KeyPhraseSearch));
            }
        }

        // Other wordings of the question, each a list of its own. Text only: a wording's pixel search
        // would add pictures whose captions none of the wordings matched.
        foreach (var extra in query.ExtraTextVectors)
        {
            if (extra.Vector.Length > 0 && extra.Weight > 0)
            {
                searches.Add((BuildSearch(extra.Vector, TextVectorName, filter, exact), extra.Weight, $"{extra.Name} → text"));

                // Every wording is searched within the countries too, not only the lead. Boosting the lead
                // alone let the boost fade as wordings were added: with the planner's three phrasings
                // the share of excerpts from the question's country fell from 87 % to 84 %, and "dashed
                // outer lines in the Philippines" was offered one Philippine excerpt instead of five.
                // Searched like this, the share rose to 91 %.
                if (countryFilter is not null)
                {
                    searches.Add((BuildSearch(extra.Vector, TextVectorName, countryFilter, exact),
                        extra.Weight * query.CountryBoostWeight, $"{extra.Name} → text [countries]"));
                }
            }
        }

        // An attached screenshot against image pixels — for a "where is this?" question, the signal.
        if (query.ImageVector is { Length: > 0 } imageVector)
        {
            searches.Add((BuildSearch(imageVector, ImageVectorName, filter, exact), query.ImageWeight, ScreenshotPixelSearch));

            // Look-alike pictures from the named country, which a screenshot's own search ranks among
            // look-alikes from everywhere else.
            if (countryFilter is not null)
            {
                searches.Add((BuildSearch(imageVector, ImageVectorName, countryFilter, exact),
                    query.CountryBoostWeight, CountryScreenshotSearch));
            }
        }

        if (searches.Count == 0)
        {
            return [];
        }

        var results = await client.QueryBatchAsync(
            collectionName,
            [.. searches.Select(search => search.Search)],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return [.. results.Select((result, index) => new WeightedHitList(
            [.. result.Result.Select(ToHit)], searches[index].Weight, searches[index].Name))];
    }

    public async Task<IReadOnlyList<KnowledgeHit>> ReadSourceChunksAsync(
        string sourceType,
        string sourceKey,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceKey))
        {
            return [];
        }

        // Both fields are keyword-indexed, so this reads the one document rather than the collection.
        var filter = BuildFilter(country: null, sourceType, ingestRun: null, sourceKey);
        var chunks = new List<KnowledgeHit>();
        var offset = default(PointId);

        do
        {
            var page = await client.ScrollAsync(
                collectionName: collectionName,
                filter: filter,
                limit: (uint)Math.Min(ScrollPageSize, limit - chunks.Count),
                payloadSelector: true,
                vectorsSelector: false,
                offset: offset,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            chunks.AddRange(page.Result.Select(point => ToHit(point.Id, score: 0f, point.Payload)));
            offset = page.NextPageOffset;
        }
        while (offset is not null && chunks.Count < limit);

        return chunks;
    }

    public async Task<IReadOnlyList<KnowledgeHit>> ReadChunksAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var points = await client.RetrieveAsync(
            collectionName,
            [.. wanted.Select(id => new PointId { Uuid = id.ToString() })],
            withPayload: true,
            withVectors: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // The store answers in its own order; the caller's order is the one the excerpts were cited in.
        var found = points
            .Select(point => ToHit(point.Id, score: 0f, point.Payload))
            .Where(hit => hit.Id != Guid.Empty)
            .ToDictionary(hit => hit.Id);

        var ordered = wanted.Where(found.ContainsKey).Select(id => found[id]).ToList();

        // Copies collapse exactly as they did when the excerpts were first offered: one list, in the
        // order asked, through the same fusion — so an excerpt comes back with every picture it had.
        return
        [
            .. KnowledgeHitFusion.Fuse([new WeightedHitList(ordered, Weight: 1d, "read by id")], ordered.Count)
                .Select(hit => hit with { Score = 0f })
        ];
    }

    public async Task<int> SweepAsync(
        string sourceType,
        string sourceKey,
        string ingestRun,
        CancellationToken cancellationToken = default)
    {
        // Everything for this source that the current run did not rewrite is stale by definition.
        var stale = new Filter
        {
            Must =
            {
                KeywordCondition("sourceType", sourceType),
                KeywordCondition("sourceKey", sourceKey)
            },
            MustNot = { KeywordCondition("ingestRun", ingestRun) }
        };

        var doomed = await client.ScrollAsync(
            collectionName: collectionName,
            filter: stale,
            limit: ScrollPageSize,
            payloadSelector: false,
            vectorsSelector: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var removed = 0;
        while (doomed.Result.Count > 0)
        {
            removed += doomed.Result.Count;
            await client.DeleteAsync(
                collectionName,
                [.. doomed.Result.Select(point => point.Id)],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Re-scroll from the start rather than paging: the deletes above have already shifted the
            // result set, so a saved offset would skip points.
            doomed = await client.ScrollAsync(
                collectionName: collectionName,
                filter: stale,
                limit: ScrollPageSize,
                payloadSelector: false,
                vectorsSelector: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        // The collection is created on the first ingest, so before then it legitimately holds
        // nothing. Reporting that as an error made a bot with an empty index look broken.
        var collections = await client.ListCollectionsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return collections.Any(name => name == collectionName)
            ? (long)await client.CountAsync(collectionName, cancellationToken: cancellationToken).ConfigureAwait(false)
            : 0;
    }

    public async Task<IReadOnlyList<string>> ListCountriesAsync(CancellationToken cancellationToken = default)
    {
        var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var payloadSelector = new WithPayloadSelector
        {
            Include = new PayloadIncludeSelector { Fields = { "country" } }
        };

        var offset = default(PointId);
        do
        {
            var page = await client.ScrollAsync(
                collectionName: collectionName,
                limit: ScrollPageSize,
                payloadSelector: payloadSelector,
                vectorsSelector: false,
                offset: offset,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var point in page.Result)
            {
                if (point.Payload.TryGetValue("country", out var value) && !string.IsNullOrWhiteSpace(value.StringValue))
                {
                    countries.Add(value.StringValue);
                }
            }

            offset = page.NextPageOffset;
        }
        while (offset is not null);

        return [.. countries.OrderBy(country => country, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<IReadOnlyList<IndexedSourceKey>> ReadSourcesMissingImageVectorsAsync(
        CancellationToken cancellationToken = default)
    {
        var collections = await client.ListCollectionsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!collections.Any(name => name == collectionName))
        {
            return [];
        }

        // An image chunk is written with its image vector when embedding succeeds and without it when
        // not, so the absence of the named vector is exactly "this picture never made it in".
        var missing = new Filter
        {
            Must = { KeywordCondition("chunkKind", "image") },
            MustNot = { new Condition { HasVector = new HasVectorCondition { HasVector = ImageVectorName } } }
        };

        var payloadSelector = new WithPayloadSelector
        {
            Include = new PayloadIncludeSelector { Fields = { "sourceType", "sourceKey" } }
        };

        var sources = new HashSet<IndexedSourceKey>();
        var offset = default(PointId);
        do
        {
            var page = await client.ScrollAsync(
                collectionName: collectionName,
                filter: missing,
                limit: ScrollPageSize,
                payloadSelector: payloadSelector,
                vectorsSelector: false,
                offset: offset,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var point in page.Result)
            {
                var sourceType = ReadString(point.Payload, "sourceType");
                var sourceKey = ReadString(point.Payload, "sourceKey");

                if (sourceType.Length > 0 && sourceKey.Length > 0)
                {
                    sources.Add(new IndexedSourceKey(sourceType, sourceKey));
                }
            }

            offset = page.NextPageOffset;
        }
        while (offset is not null);

        return [.. sources.OrderBy(source => source.SourceType, StringComparer.Ordinal)
            .ThenBy(source => source.SourceKey, StringComparer.Ordinal)];
    }

    private QueryPoints BuildSearch(ReadOnlyMemory<float> vector, string vectorName, Filter? filter, bool exact)
    {
        var search = new QueryPoints
        {
            CollectionName = collectionName,
            Query = vector.ToArray(),
            Using = vectorName,
            Limit = PrefetchLimit,
            WithPayload = new WithPayloadSelector { Enable = true },
            // Exact rather than approximate. The approximate index missed the chunk that answers
            // "is pampa's grass a real meta?" — scoring 0.372 against a tenth place of 0.359, it was
            // not among the first 300 returned. A full scan of the 35k points took 22 ms for both
            // searches on the production server, against 9 ms approximate: worth it for a question
            // that then spends seconds with the model.
            Params = new SearchParams { Exact = exact }
        };

        if (filter is not null)
        {
            search.Filter = filter;
        }

        return search;
    }

    private static PointStruct ToPointStruct(KnowledgePoint point, string ingestRun)
    {
        var chunk = point.Chunk;

        var payload = new Dictionary<string, Value>
        {
            ["sourceType"] = chunk.SourceType,
            ["sourceKey"] = chunk.SourceKey,
            ["localKey"] = chunk.LocalKey,
            ["chunkKind"] = chunk.Kind.ToString().ToLowerInvariant(),
            ["text"] = chunk.Text,
            ["sourceUrl"] = chunk.SourceUrl,
            ["imageUrl"] = chunk.ImageUrl ?? string.Empty,
            ["title"] = chunk.Title ?? string.Empty,
            // Normalised so a filter does not have to care how a source spelled it.
            ["country"] = chunk.Country?.ToLowerInvariant() ?? string.Empty,
            ["sectionPath"] = chunk.SectionPath ?? string.Empty,
            ["author"] = chunk.Author ?? string.Empty,
            ["priority"] = chunk.Priority,
            ["ingestRun"] = ingestRun
        };

        var vectors = new Dictionary<string, float[]> { [TextVectorName] = point.TextVector.ToArray() };
        if (point.ImageVector is { } imageVector)
        {
            vectors[ImageVectorName] = imageVector.ToArray();
        }

        return new PointStruct
        {
            Id = new PointId { Uuid = chunk.PointId.ToString() },
            Vectors = vectors,
            Payload = { payload }
        };
    }

    private static Filter? BuildFilter(
        string? country,
        string? sourceType,
        string? ingestRun,
        string? sourceKey,
        KnowledgeChunkKind? chunkKind = null)
    {
        var filter = new Filter();

        if (chunkKind is { } kind)
        {
            // Written lower-cased, as ToPointStruct stores it.
            filter.Must.Add(KeywordCondition("chunkKind", kind.ToString().ToLowerInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            filter.Must.Add(KeywordCondition("country", country.ToLowerInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            filter.Must.Add(KeywordCondition("sourceType", sourceType));
        }

        if (!string.IsNullOrWhiteSpace(sourceKey))
        {
            filter.Must.Add(KeywordCondition("sourceKey", sourceKey));
        }

        if (!string.IsNullOrWhiteSpace(ingestRun))
        {
            filter.Must.Add(KeywordCondition("ingestRun", ingestRun));
        }

        return filter.Must.Count == 0 ? null : filter;
    }

    private static Condition KeywordCondition(string field, string value) =>
        new() { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } } };

    /// <summary><paramref name="filter"/>, further limited to any of <paramref name="countries"/>.</summary>
    private static Filter WithCountries(Filter? filter, IReadOnlyList<string> countries)
    {
        var narrowed = filter?.Clone() ?? new Filter();
        narrowed.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key = "country",
                // Lower-cased like the payload, which is normalised on write.
                Match = new Match { Keywords = new RepeatedStrings { Strings = { countries.Select(country => country.ToLowerInvariant()) } } }
            }
        });

        return narrowed;
    }

    /// <summary>
    /// <paramref name="filter"/>, further limited to chunks whose text contains any of
    /// <paramref name="phrases"/>, or null when there are none.
    ///
    /// The text field has no full-text index, and without one Qdrant matches the phrase as a plain,
    /// case-sensitive substring of the text — which is exactly what is wanted here: no tokeniser
    /// deciding what a word is, and no index to build on a production collection. Case is covered by
    /// asking for the phrase as written ("B-type"), in lower case, and with a capital, the way it
    /// opens a sentence.
    /// </summary>
    private static Filter? WithPhrases(Filter? filter, IReadOnlyList<string> phrases)
    {
        var variants = phrases
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .Select(phrase => phrase.Trim())
            .SelectMany(phrase => new[] { phrase, phrase.ToLowerInvariant(), Capitalised(phrase.ToLowerInvariant()) })
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (variants.Count == 0)
        {
            return null;
        }

        var narrowed = filter?.Clone() ?? new Filter();
        var anyPhrase = new Filter();
        foreach (var variant in variants)
        {
            anyPhrase.Should.Add(new Condition { Field = new FieldCondition { Key = "text", Match = new Match { Text = variant } } });
        }

        narrowed.Must.Add(new Condition { Filter = anyPhrase });
        return narrowed;
    }

    private static string Capitalised(string phrase) =>
        phrase.Length == 0 ? phrase : string.Concat(char.ToUpperInvariant(phrase[0]).ToString(), phrase[1..]);

    private static KnowledgeHit ToHit(ScoredPoint point) => ToHit(point.Id, point.Score, point.Payload);

    private static KnowledgeHit ToHit(PointId? pointId, float score, IReadOnlyDictionary<string, Value> payload) =>
        new(
            Guid.TryParse(pointId?.Uuid, out var id) ? id : Guid.Empty,
            score,
            ReadString(payload, "chunkKind") == "image" ? KnowledgeChunkKind.Image : KnowledgeChunkKind.Text,
            ReadString(payload, "text"),
            ReadString(payload, "sourceUrl"),
            NullIfEmpty(ReadString(payload, "imageUrl")),
            NullIfEmpty(ReadString(payload, "title")),
            NullIfEmpty(ReadString(payload, "country")),
            NullIfEmpty(ReadString(payload, "sectionPath")),
            NullIfEmpty(ReadString(payload, "author")),
            payload.TryGetValue("priority", out var priority) ? (int)priority.IntegerValue : 0,
            // Where the chunk came from, so the rest of its guide can be read back.
            NullIfEmpty(ReadString(payload, "sourceType")),
            NullIfEmpty(ReadString(payload, "sourceKey")),
            NullIfEmpty(ReadString(payload, "localKey")));

    /// <summary>
    /// Reads a payload string defensively. A point written by an older schema version is missing keys
    /// rather than malformed, and one absent field should not throw away an otherwise good result.
    /// </summary>
    private static string ReadString(IReadOnlyDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var value) ? value.StringValue ?? string.Empty : string.Empty;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}

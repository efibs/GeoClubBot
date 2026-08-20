using FluentAssertions;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.Integration.AI;

/// <summary>
/// Exercises the vector index against a real Qdrant instance. NSubstitute cannot fake
/// <c>QdrantClient</c> (its methods are sealed interface implementations), and the behaviour that
/// matters here — named vectors, reciprocal-rank fusion, filtered scrolling — lives in the server
/// rather than in our code, so a container is the only way to prove any of it.
///
/// Each test takes a fresh collection so the shared container can be reused safely. Vectors are
/// synthetic unit vectors, which makes cosine similarity exactly predictable.
/// </summary>
[Collection(QdrantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class QdrantKnowledgeIndexIntegrationTests(QdrantFixture fixture)
{
    private const int VectorSize = 32;

    [Fact]
    public async Task EnsureCollection_IsIdempotent()
    {
        var index = fixture.CreateKnowledgeIndex(QdrantFixture.NewCollectionName(), VectorSize);

        await index.EnsureCollectionAsync();
        await index.EnsureCollectionAsync();

        (await index.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Upsert_AcceptsPointsWithAndWithoutAnImageVector()
    {
        // Text chunks legitimately have no image vector. Qdrant allows a point to carry a subset of
        // the collection's named vectors, and the schema depends on that.
        var index = await CreateIndexAsync();

        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("a", KnowledgeChunkKind.Text), Axis(0)),
            new KnowledgePoint(Chunk("b", KnowledgeChunkKind.Image, imageUrl: "https://i.imgur.com/b.png"), Axis(1), Axis(2))
        ], ingestRun: "run-1");

        (await index.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Upsert_IsIdempotent_ForUnchangedChunks()
    {
        // Point ids are derived from the chunk's natural key, so re-ingesting the same content must
        // update in place. Random ids would silently double the collection on every run.
        var index = await CreateIndexAsync();
        var point = new KnowledgePoint(Chunk("stable", KnowledgeChunkKind.Text), Axis(0));

        await index.UpsertAsync([point], "run-1");
        await index.UpsertAsync([point], "run-2");

        (await index.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Search_ByText_RanksTheClosestCaptionFirst()
    {
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("exact", KnowledgeChunkKind.Text, text: "bollards"), Axis(0)),
            new KnowledgePoint(Chunk("half", KnowledgeChunkKind.Text, text: "poles"), Between(0, 1)),
            new KnowledgePoint(Chunk("unrelated", KnowledgeChunkKind.Text, text: "trees"), Axis(5))
        ], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), Limit = 3 });

        hits.Select(hit => hit.Text).Should().ContainInOrder("bollards", "poles");
    }

    [Fact]
    public async Task Search_ByImage_FindsAVisuallySimilarImage_NotItsCaption()
    {
        // The "post a screenshot" path: the caption is deliberately unhelpful, so a match can only
        // come from the image vector.
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(
                Chunk("map", KnowledgeChunkKind.Image, text: "unhelpful caption", imageUrl: "https://i.imgur.com/map.png"),
                Axis(9), Axis(0)),
            new KnowledgePoint(Chunk("prose", KnowledgeChunkKind.Text, text: "lots of relevant words"), Axis(9))
        ], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery { ImageVector = Axis(0), Limit = 5 });

        hits.Should().ContainSingle("only one point carries an image vector");
        hits[0].Kind.Should().Be(KnowledgeChunkKind.Image);
        hits[0].ImageUrl.Should().Be("https://i.imgur.com/map.png");
    }

    [Fact]
    public async Task Search_ByText_FusesBothModalities_SoAnImageCanOutrankProse()
    {
        // Fusion is rank-based, so an image that ranks first within the image prefetch competes with
        // the top text hit even though its raw cross-modal score is much lower. Scored addition would
        // bury it.
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(
                Chunk("infographic", KnowledgeChunkKind.Image, text: "sparse caption", imageUrl: "https://i.imgur.com/i.png"),
                Axis(7), Axis(0)),
            new KnowledgePoint(Chunk("weak-prose", KnowledgeChunkKind.Text, text: "loosely related prose"), Between(0, 3))
        ], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), Limit = 5 });

        hits.Should().HaveCount(2);
        hits.Should().Contain(hit => hit.Kind == KnowledgeChunkKind.Image,
            "the cross-modal prefetch is what lets a thin-captioned image surface at all");
    }

    [Fact]
    public async Task Search_RespectsTheCountryFilter()
    {
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("tn", KnowledgeChunkKind.Text, text: "tunisian bollards", country: "Tunisia"), Axis(0)),
            new KnowledgePoint(Chunk("ke", KnowledgeChunkKind.Text, text: "kenyan bollards", country: "Kenya"), Axis(0))
        ], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), Country = "tunisia", Limit = 5 });

        hits.Should().ContainSingle();
        hits[0].Text.Should().Be("tunisian bollards");
    }

    [Fact]
    public async Task Search_MatchesCountryCaseInsensitively()
    {
        // Sources spell countries inconsistently, so the payload is normalised on write and the
        // filter normalises on read.
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
            [new KnowledgePoint(Chunk("tn", KnowledgeChunkKind.Text, country: "TUNISIA"), Axis(0))], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), Country = "Tunisia" });

        hits.Should().ContainSingle();
    }

    [Fact]
    public async Task Sweep_RemovesOnlyStalePointsOfThatSource()
    {
        var index = await CreateIndexAsync();

        // Run 1 writes two chunks for a source, plus an unrelated source that must not be touched.
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("kept", KnowledgeChunkKind.Text, sourceKey: "tunisia"), Axis(0)),
            new KnowledgePoint(Chunk("dropped", KnowledgeChunkKind.Text, sourceKey: "tunisia"), Axis(1)),
            new KnowledgePoint(Chunk("other", KnowledgeChunkKind.Text, sourceKey: "kenya"), Axis(2))
        ], "run-1");

        // Run 2 re-produces only one of them: the guide lost a section.
        await index.UpsertAsync(
            [new KnowledgePoint(Chunk("kept", KnowledgeChunkKind.Text, sourceKey: "tunisia"), Axis(0))], "run-2");

        var removed = await index.SweepAsync("plonkit", "tunisia", "run-2");

        removed.Should().Be(1);
        (await index.CountAsync()).Should().Be(2, "the other source must survive a sweep of this one");

        var remaining = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(1), Limit = 5 });
        remaining.Should().NotContain(hit => hit.Text.Contains("dropped"));
    }

    [Fact]
    public async Task Sweep_DeletesMoreThanOneScrollPage()
    {
        // The sweep re-scrolls after each delete rather than paging with a saved offset, because
        // deleting shifts the result set underneath a stored cursor.
        const int count = 300;
        var index = await CreateIndexAsync();

        var stale = Enumerable.Range(0, count)
            .Select(i => new KnowledgePoint(Chunk($"chunk-{i}", KnowledgeChunkKind.Text), Axis(i % VectorSize)))
            .ToList();

        await index.UpsertAsync(stale, "run-1");
        await index.UpsertAsync([new KnowledgePoint(Chunk("chunk-0", KnowledgeChunkKind.Text), Axis(0))], "run-2");

        var removed = await index.SweepAsync("plonkit", "tunisia", "run-2");

        removed.Should().Be(count - 1);
        (await index.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ListCountries_DedupesAndSorts_AcrossScrollPages()
    {
        const int count = 300;
        var index = await CreateIndexAsync();

        var points = Enumerable.Range(0, count)
            .Select(i => new KnowledgePoint(
                Chunk($"chunk-{i}", KnowledgeChunkKind.Text, country: (i % 3) switch { 0 => "Tunisia", 1 => "Kenya", _ => "Japan" }),
                Axis(i % VectorSize)))
            .ToList();

        await index.UpsertAsync(points, "run-1");

        var countries = await index.ListCountriesAsync();

        countries.Should().Equal("japan", "kenya", "tunisia");
    }

    [Fact]
    public async Task Search_ReturnsNothing_WhenNoVectorIsSupplied()
    {
        var index = await CreateIndexAsync();
        await index.UpsertAsync([new KnowledgePoint(Chunk("a", KnowledgeChunkKind.Text), Axis(0))], "run-1");

        (await index.SearchAsync(new KnowledgeQuery())).Should().BeEmpty();
    }

    private async Task<Infrastructure.OutputAdapters.AI.QdrantKnowledgeIndex> CreateIndexAsync()
    {
        var index = fixture.CreateKnowledgeIndex(QdrantFixture.NewCollectionName(), VectorSize);
        await index.EnsureCollectionAsync();
        return index;
    }

    private static KnowledgeChunk Chunk(
        string localKey,
        KnowledgeChunkKind kind,
        string? text = null,
        string? imageUrl = null,
        string? country = null,
        string sourceKey = "tunisia") =>
        new()
        {
            SourceType = "plonkit",
            SourceKey = sourceKey,
            LocalKey = localKey,
            Kind = kind,
            Text = text ?? localKey,
            SourceUrl = $"https://www.plonkit.net/{sourceKey}#{localKey}",
            ImageUrl = imageUrl,
            Country = country ?? "Tunisia",
            Title = "Tunisia",
            SectionPath = "Tunisia > Identifying"
        };

    /// <summary>Unit vector along one axis; cosine similarity against a different axis is exactly 0.</summary>
    private static ReadOnlyMemory<float> Axis(int index)
    {
        var vector = new float[VectorSize];
        vector[index % VectorSize] = 1f;
        return vector;
    }

    /// <summary>Unit vector halfway between two axes; cosine similarity against either is ~0.707.</summary>
    private static ReadOnlyMemory<float> Between(int a, int b)
    {
        var vector = new float[VectorSize];
        vector[a] = vector[b] = (float)(1 / Math.Sqrt(2));
        return vector;
    }
}

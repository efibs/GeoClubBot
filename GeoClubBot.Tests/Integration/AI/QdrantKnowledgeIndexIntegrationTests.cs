using FluentAssertions;
using Infrastructure.OutputAdapters.AI;
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
    public async Task Count_ReportsZero_BeforeTheCollectionHasBeenCreated()
    {
        // The collection is created on the first ingest. Treating its absence as an error made a bot
        // that simply had not indexed anything yet report its index as "unavailable", which reads as
        // a fault rather than an empty start.
        var index = fixture.CreateKnowledgeIndex(QdrantFixture.NewCollectionName(), VectorSize);

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
    public async Task Search_ByText_StillReachesAThinlyCaptionedImage_ThroughItsPixels()
    {
        // Fusion is rank-based, so an image found by its pixels competes with prose even though its
        // raw cross-modal score is much lower; scored addition would bury it. It is weighted down so
        // prose the question is about comes first, but it must still be reachable.
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(
                Chunk("infographic", KnowledgeChunkKind.Image, text: "sparse caption", imageUrl: "https://i.imgur.com/i.png"),
                Axis(7), Axis(0)),
            new KnowledgePoint(Chunk("weak-prose", KnowledgeChunkKind.Text, text: "loosely related prose"), Between(0, 3))
        ], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), Limit = 5 });

        hits.Select(hit => hit.Text).Should().Equal(["loosely related prose", "sparse caption"],
            "the prose matches the question's text; the picture only its pixels");
    }

    [Fact]
    public async Task Search_OffersCopiesOfTheSameTextOnce_KeepingThePicture()
    {
        // The same sentence captioning a picture and standing as prose took two of eight slots; one
        // excerpt now carries it, and the one the model can attach.
        var index = await CreateIndexAsync();

        // Similarity 0.5 to the question: below the picture's 0.707, so the order is not a tie.
        var lessRelated = new float[VectorSize];
        lessRelated[0] = 0.5f;
        lessRelated[2] = (float)Math.Sqrt(0.75);

        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("prose", KnowledgeChunkKind.Text, text: "Khasi pines grow in Meghalaya."), Axis(0)),
            new KnowledgePoint(
                Chunk("picture", KnowledgeChunkKind.Image, text: "**Khasi pines** grow in  Meghalaya.",
                    imageUrl: "https://i.imgur.com/khasi.png"),
                Between(0, 1), Axis(4)),
            new KnowledgePoint(Chunk("other", KnowledgeChunkKind.Text, text: "Chir pines grow in Uttarakhand."), lessRelated)
        ], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), Limit = 5 });

        hits.Should().HaveCount(2);
        hits[0].ImageUrl.Should().Be("https://i.imgur.com/khasi.png");
        hits[1].Text.Should().Be("Chir pines grow in Uttarakhand.");
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

    [Fact]
    public async Task Search_IgnoresAnEmptyVector_RatherThanFailingTheWholeQuery()
    {
        // An empty vector reached the store as a real prefetch and Qdrant rejected the entire request
        // — "expected dim: N, got 0" — so a perfectly good text search died on an image vector that
        // was never supplied. An empty vector carries no query, so it contributes no prefetch.
        var index = await CreateIndexAsync();
        await index.UpsertAsync([new KnowledgePoint(Chunk("a", KnowledgeChunkKind.Text), Axis(0))], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery
        {
            TextVector = Axis(0),
            ImageVector = ReadOnlyMemory<float>.Empty,
            Limit = 3
        });

        hits.Should().ContainSingle();
    }

    [Fact]
    public async Task ReadSourcesMissingImageVectors_FindsImagesThatWereNeverEmbedded()
    {
        // An image chunk is written without its image vector when embedding fails, so the missing vector
        // is the only durable record that a picture never made it in. Text chunks never carry one.
        var index = await CreateIndexAsync();

        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("prose", KnowledgeChunkKind.Text, sourceKey: "tunisia"), Axis(0)),
            new KnowledgePoint(
                Chunk("embedded", KnowledgeChunkKind.Image, imageUrl: "https://i.imgur.com/ok.png", sourceKey: "tunisia"),
                Axis(1), Axis(2)),
            new KnowledgePoint(
                Chunk("lost", KnowledgeChunkKind.Image, imageUrl: "https://i.imgur.com/lost.png", sourceKey: "kenya"),
                Axis(3)),
            new KnowledgePoint(
                Chunk("lost-too", KnowledgeChunkKind.Image, imageUrl: "https://i.imgur.com/lost2.png", sourceKey: "kenya"),
                Axis(4))
        ], "run-1");

        var missing = await index.ReadSourcesMissingImageVectorsAsync();

        missing.Should().Equal(new IndexedSourceKey("plonkit", "kenya"));
    }

    [Fact]
    public async Task ReadSourcesMissingImageVectors_IsEmpty_BeforeTheCollectionExists()
    {
        var index = fixture.CreateKnowledgeIndex(QdrantFixture.NewCollectionName(), VectorSize);

        (await index.ReadSourcesMissingImageVectorsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_BoostsTheCountriesTheQuestionIsAbout_WithoutShuttingOutOthers()
    {
        // "Is this bollard a meta in Australia?" was offered four US bollards. A boost searches the
        // question again within Australia and fuses that list in: the Australian chunk rises above a
        // closer American one, and the American one is still offered.
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("us", KnowledgeChunkKind.Text, text: "US bollards", country: "usa"), Axis(0)),
            new KnowledgePoint(Chunk("au", KnowledgeChunkKind.Text, text: "Australian bollards", country: "Australia"), Between(0, 1)),
            new KnowledgePoint(Chunk("nz", KnowledgeChunkKind.Text, text: "NZ bollards", country: "New Zealand"), Axis(2))
        ], "run-1");

        var unboosted = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), Limit = 3 });
        var boosted = await index.SearchAsync(new KnowledgeQuery
        {
            TextVector = Axis(0),
            BoostCountries = ["australia"],
            Limit = 3
        });

        unboosted[0].Text.Should().Be("US bollards");
        boosted.Select(hit => hit.Text).Should().Equal("Australian bollards", "US bollards", "NZ bollards");
    }

    [Fact]
    public async Task Search_BoostedCountries_NarrowTheHardFilter_RatherThanReplaceIt()
    {
        // /ai search country: must still return that country only, whatever else the query carries.
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("tn", KnowledgeChunkKind.Text, text: "tunisian bollards", country: "Tunisia"), Axis(0)),
            new KnowledgePoint(Chunk("ke", KnowledgeChunkKind.Text, text: "kenyan bollards", country: "Kenya"), Axis(0))
        ], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery
        {
            TextVector = Axis(0),
            Country = "kenya",
            BoostCountries = ["tunisia"],
            Limit = 5
        });

        hits.Select(hit => hit.Text).Should().Equal("kenyan bollards");
    }

    [Fact]
    public async Task SearchLists_SearchesEveryWordingCountryAndPhrase_InOneBatch_AtTheirWeights()
    {
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("a", KnowledgeChunkKind.Image, text: "a picture of a pole", imageUrl: "https://i/a.png",
                country: "Brazil"), Axis(0), Axis(3))
        ], "run-1");

        var lists = await index.SearchListsAsync(new KnowledgeQuery
        {
            TextVector = Axis(0),
            TextWeight = 0.8,
            ImageVector = Axis(3),
            ImageWeight = 0.5,
            ExtraTextVectors = [new WeightedQueryVector("conversation", Axis(1), 1d), new WeightedQueryVector("unweighted", Axis(2), 0d)],
            BoostCountries = ["Brazil"],
            CountryBoostWeight = 2,
            KeyPhrases = ["pole"],
            KeyPhraseWeight = 0.5
        });

        lists.Select(list => (list.Name, list.Weight)).Should().BeEquivalentTo(new[]
        {
            (QdrantKnowledgeIndex.QuestionTextSearch, 0.8),
            (QdrantKnowledgeIndex.QuestionPixelSearch, 0.8 * 0.25),
            (QdrantKnowledgeIndex.CountryTextSearch, 0.8 * 2),
            (QdrantKnowledgeIndex.KeyPhraseSearch, 0.5),
            ("conversation → text", 1d),
            // Every wording within the countries too: boosting the lead alone let the boost fade as
            // wordings were added.
            ("conversation → text [countries]", 1d * 2),
            (QdrantKnowledgeIndex.ScreenshotPixelSearch, 0.5),
            (QdrantKnowledgeIndex.CountryScreenshotSearch, 2d)
        }, "a wording that counts for nothing is not searched");
        lists.Should().OnlyContain(list => list.Hits.Count == 1);
    }

    [Fact]
    public async Task Search_FindsAKeyPhrase_AsAPlainSubstring_WithoutAFullTextIndex()
    {
        // "Asymmetrical crossbar with no supports" ranked 117th for "which states in Brazil don't do
        // pole supports?". The text field has no full-text index, and without one Qdrant matches the
        // phrase as a case-sensitive substring — so the phrase is asked for as written, in lower case
        // and capitalised.
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("close", KnowledgeChunkKind.Text, text: "Pole supports are common in Paraná."), Axis(0)),
            new KnowledgePoint(Chunk("lower", KnowledgeChunkKind.Text, text: "An asymmetrical crossbar with no supports."), Axis(5)),
            new KnowledgePoint(Chunk("capital", KnowledgeChunkKind.Text, text: "No supports at all in Ceará."), Axis(6)),
            new KnowledgePoint(Chunk("case", KnowledgeChunkKind.Text, text: "Thick B-type guardrails."), Axis(7)),
            new KnowledgePoint(Chunk("none", KnowledgeChunkKind.Text, text: "Nosupportsglued."), Axis(8)),
            // A substring, not a set of words: "insulator" is found inside "insulators".
            new KnowledgePoint(Chunk("plural", KnowledgeChunkKind.Text, text: "Brazil has single insulators at the top."), Axis(9))
        ], "run-1");

        var lists = await index.SearchListsAsync(new KnowledgeQuery
        {
            TextVector = Axis(0),
            KeyPhrases = ["No Supports", "B-type", "single insulator"],
            KeyPhraseWeight = 1
        });

        lists.Single(list => list.Name == QdrantKnowledgeIndex.KeyPhraseSearch).Hits.Select(hit => hit.Text)
            .Should().BeEquivalentTo(
            [
                "An asymmetrical crossbar with no supports.", "No supports at all in Ceará.", "Thick B-type guardrails.",
                "Brazil has single insulators at the top."
            ]);
    }

    [Fact]
    public async Task Search_SearchesNoPhraseList_WhenThereIsNoPhrase()
    {
        var index = await CreateIndexAsync();
        await index.UpsertAsync([new KnowledgePoint(Chunk("a", KnowledgeChunkKind.Text), Axis(0))], "run-1");

        var lists = await index.SearchListsAsync(new KnowledgeQuery { TextVector = Axis(0), KeyPhrases = ["", "  "] });

        lists.Should().NotContain(list => list.Name == QdrantKnowledgeIndex.KeyPhraseSearch);
    }

    [Fact]
    public async Task Search_DemotesAPictureWhoseOnlyTextIsAHeading()
    {
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("heading", KnowledgeChunkKind.Image, text: "Distribution", imageUrl: "https://i/d.png"), Axis(0), Axis(9)),
            new KnowledgePoint(Chunk("tip", KnowledgeChunkKind.Text, text: "The tent with 4 straps covers central Mongolia."), Between(0, 1))
        ], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), HeadingOnlyPictureWeight = 0.25, Limit = 2 });

        hits.Select(hit => hit.Text).Should().Equal("The tent with 4 straps covers central Mongolia.", "Distribution");
    }

    [Fact]
    public async Task ReadSourceChunks_ReadsOneGuide_WithWhereEachChunkCameFrom()
    {
        // The answer's read-the-guide tool: the rest of the guide an excerpt came from, by the two
        // payload fields the collection indexes.
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("p1", KnowledgeChunkKind.Text), Axis(0)),
            new KnowledgePoint(Chunk("p2", KnowledgeChunkKind.Text), Axis(1)),
            new KnowledgePoint(Chunk("p3", KnowledgeChunkKind.Image, imageUrl: "https://relay/p3.png"), Axis(2), Axis(3)),
            new KnowledgePoint(Chunk("p1", KnowledgeChunkKind.Text, sourceKey: "algeria"), Axis(4)),
            new KnowledgePoint(Chunk("p1", KnowledgeChunkKind.Text) with { SourceType = "rmrg" }, Axis(5))
        ], "run-1");

        var chunks = await index.ReadSourceChunksAsync("plonkit", "tunisia", limit: 100);

        chunks.Select(chunk => chunk.LocalKey).Should().BeEquivalentTo(["p1", "p2", "p3"]);
        chunks.Should().OnlyContain(chunk => chunk.SourceType == "plonkit" && chunk.SourceKey == "tunisia" && chunk.Score == 0f);
        chunks.Single(chunk => chunk.LocalKey == "p3").Should().BeEquivalentTo(new
        {
            Kind = KnowledgeChunkKind.Image,
            ImageUrl = "https://relay/p3.png",
            SectionPath = "Tunisia > Identifying"
        });
    }

    [Fact]
    public async Task ReadSourceChunks_PagesPastOneScrollPage_UpToItsLimit()
    {
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
            [.. Enumerable.Range(0, 300).Select(i => new KnowledgePoint(Chunk($"p{i}", KnowledgeChunkKind.Text), Axis(i)))],
            "run-1");

        (await index.ReadSourceChunksAsync("plonkit", "tunisia", limit: 1000)).Should().HaveCount(300);
        (await index.ReadSourceChunksAsync("plonkit", "tunisia", limit: 10)).Should().HaveCount(10);
        (await index.ReadSourceChunksAsync("plonkit", "morocco", limit: 1000)).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_KeepsEveryPictureOfASlidesCopies_InOneExcerpt()
    {
        // A slide's caption sits beside each of its pictures; offered as one excerpt, it brings them all.
        var index = await CreateIndexAsync();
        const string caption = "India Poles > Slide 18: concrete poles with one crossbar.";
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("s18-1", KnowledgeChunkKind.Image, text: caption, imageUrl: "https://relay/1.png", anchor: "slide=id.p18"), Axis(0), Axis(5)),
            new KnowledgePoint(Chunk("s18-2", KnowledgeChunkKind.Image, text: caption, imageUrl: "https://relay/2.png", anchor: "slide=id.p18"), Axis(0), Axis(6)),
            new KnowledgePoint(Chunk("s18-3", KnowledgeChunkKind.Image, text: caption, imageUrl: "https://relay/3.png", anchor: "slide=id.p18"), Axis(0), Axis(7)),
            new KnowledgePoint(Chunk("other", KnowledgeChunkKind.Text, text: "Kerala poles are tall."), Between(0, 1))
        ], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), Limit = 5 });

        hits.Should().HaveCount(2);
        new[] { hits[0].ImageUrl! }.Concat(hits[0].ExtraImageUrls).Should()
            .BeEquivalentTo(["https://relay/1.png", "https://relay/2.png", "https://relay/3.png"]);
        hits[0].ChunkIds.Should().HaveCount(3, "each copy's id is kept, so the excerpt can be read back whole");
    }

    [Fact]
    public async Task Search_NeverShowsAnotherGuidesPicture_AsAnExtraPictureOfAnExcerpt()
    {
        // Two decks each have a slide captioned only "Bollards". Folded into one excerpt, the Czech
        // bollard was shown under the Polish deck's title and link.
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("pl-1", KnowledgeChunkKind.Image, text: "Bollards", imageUrl: "https://relay/pl.png",
                sourceKey: "poland", anchor: "slide=id.p4"), Axis(0), Axis(5)),
            new KnowledgePoint(Chunk("cz-1", KnowledgeChunkKind.Image, text: "Bollards", imageUrl: "https://relay/cz.png",
                sourceKey: "czechia", anchor: "slide=id.p9"), Axis(0), Axis(6))
        ], "run-1");

        var hit = (await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), Limit = 5 })).Single();

        new[] { hit.ImageUrl! }.Concat(hit.ExtraImageUrls).Should().ContainSingle()
            .Which.Should().Be(hit.SourceUrl.Contains("poland", StringComparison.Ordinal) ? "https://relay/pl.png" : "https://relay/cz.png");
    }

    [Fact]
    public async Task ReadChunks_ReadsWhatAnAnswerCited_InTheOrderItWasCited()
    {
        // A follow-up is offered again what the answer before it cited, read by id rather than searched.
        var index = await CreateIndexAsync();
        var map = Chunk("map", KnowledgeChunkKind.Image, text: "Four straps near Khorgo.", imageUrl: "https://relay/map.png");
        var tent = Chunk("tent", KnowledgeChunkKind.Text, text: "Open tent between the strap sections.");
        var other = Chunk("other", KnowledgeChunkKind.Text, text: "Mongolian plates are blurred.");
        await index.UpsertAsync(
        [
            new KnowledgePoint(map, Axis(0), Axis(1)),
            new KnowledgePoint(tent, Axis(2)),
            new KnowledgePoint(other, Axis(3))
        ], "run-1");

        var hits = await index.ReadChunksAsync([tent.PointId, map.PointId]);

        hits.Select(hit => hit.LocalKey).Should().Equal("tent", "map");
        hits.Should().OnlyContain(hit => hit.Score == 0f && hit.SourceType == "plonkit" && hit.SourceKey == "tunisia");
        hits[1].Should().BeEquivalentTo(new { Kind = KnowledgeChunkKind.Image, ImageUrl = "https://relay/map.png", Text = "Four straps near Khorgo." });
    }

    [Fact]
    public async Task ReadChunks_CollapsesTheCopiesOfOneExcerpt_KeepingTheirPictures()
    {
        // An answer citing a slide stores the ids of every copy merged into it; read back, they are one
        // excerpt again, with every picture.
        var index = await CreateIndexAsync();
        const string caption = "Brazilian Poles Lite > Slide 7: Roraima, antenna common.";
        var first = Chunk("s7-1", KnowledgeChunkKind.Image, text: caption, imageUrl: "https://relay/a.png", anchor: "slide=id.p7");
        var second = Chunk("s7-2", KnowledgeChunkKind.Image, text: caption, imageUrl: "https://relay/b.png", anchor: "slide=id.p7");
        var text = Chunk("t", KnowledgeChunkKind.Text, text: "Gen 4 in Boa Vista.");
        await index.UpsertAsync(
        [
            new KnowledgePoint(first, Axis(0), Axis(1)),
            new KnowledgePoint(second, Axis(0), Axis(2)),
            new KnowledgePoint(text, Axis(3))
        ], "run-1");

        var hits = await index.ReadChunksAsync([first.PointId, second.PointId, text.PointId]);

        hits.Should().HaveCount(2);
        hits[0].ImageUrl.Should().Be("https://relay/a.png");
        hits[0].ExtraImageUrls.Should().Equal("https://relay/b.png");
        hits[0].ChunkIds.Should().Equal(first.PointId, second.PointId);
        hits[1].LocalKey.Should().Be("t");
    }

    [Fact]
    public async Task ReadChunks_LeavesOutWhatIsNoLongerInTheIndex()
    {
        // A guide re-indexed since, or a species range from a tool that never was in the index.
        var index = await CreateIndexAsync();
        var kept = Chunk("kept", KnowledgeChunkKind.Text, text: "Still here.");
        await index.UpsertAsync([new KnowledgePoint(kept, Axis(0))], "run-1");

        var hits = await index.ReadChunksAsync([Guid.NewGuid(), kept.PointId, Guid.Empty, kept.PointId]);

        hits.Should().ContainSingle().Which.LocalKey.Should().Be("kept");
        (await index.ReadChunksAsync([])).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_CarriesWhereEachHitCameFrom()
    {
        var index = await CreateIndexAsync();
        await index.UpsertAsync([new KnowledgePoint(Chunk("abc", KnowledgeChunkKind.Text), Axis(0))], "run-1");

        var hit = (await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0) })).Single();

        hit.Should().BeEquivalentTo(new { SourceType = "plonkit", SourceKey = "tunisia", LocalKey = "abc" });
    }

    [Fact]
    public async Task Search_FindsOnlyPictures_WhenAskedFor()
    {
        var index = await CreateIndexAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("text", KnowledgeChunkKind.Text, text: "Tunisian bollards"), Axis(0)),
            new KnowledgePoint(Chunk("picture", KnowledgeChunkKind.Image, text: "A Tunisian bollard", imageUrl: "https://relay/b.png"),
                Between(0, 1), Axis(2))
        ], "run-1");

        var hits = await index.SearchAsync(new KnowledgeQuery { TextVector = Axis(0), ChunkKind = KnowledgeChunkKind.Image });

        hits.Should().ContainSingle().Which.LocalKey.Should().Be("picture");
    }

    private async Task<Infrastructure.OutputAdapters.AI.QdrantKnowledgeIndex> CreateIndexAsync()
    {
        var index = fixture.CreateKnowledgeIndex(QdrantFixture.NewCollectionName(), VectorSize);
        await index.EnsureCollectionAsync();
        return index;
    }

    /// <param name="anchor">The place in the guide its link leads to; its own key unless it shares one, as a slide's pictures do.</param>
    private static KnowledgeChunk Chunk(
        string localKey,
        KnowledgeChunkKind kind,
        string? text = null,
        string? imageUrl = null,
        string? country = null,
        string sourceKey = "tunisia",
        string? anchor = null) =>
        new()
        {
            SourceType = "plonkit",
            SourceKey = sourceKey,
            LocalKey = localKey,
            Kind = kind,
            Text = text ?? localKey,
            SourceUrl = $"https://www.plonkit.net/{sourceKey}#{anchor ?? localKey}",
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

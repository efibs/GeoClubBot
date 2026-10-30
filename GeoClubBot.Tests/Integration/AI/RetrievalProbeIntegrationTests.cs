using Configuration;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using GeoClubBot.RetrievalProbe;
using Infrastructure.OutputAdapters.AI;
using NSubstitute;
using Qdrant.Client.Grpc;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Feedback;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.AI;

/// <summary>
/// The retrieval probe against a real Qdrant. It is pointed at production, so the property that
/// matters most is proven here rather than asserted in a comment: reads reach the index, and no write
/// does. The commands run end to end too, with a stand-in embedder, so no network is involved.
/// </summary>
[Collection(QdrantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RetrievalProbeIntegrationTests(QdrantFixture fixture) : IDisposable
{
    private const int VectorSize = 32;

    private readonly string _workDirectory = Directory.CreateTempSubdirectory("retrieval-probe-").FullName;

    [Fact]
    public async Task Guard_LetsTheBotsOwnSearchThrough()
    {
        var collection = await SeedAsync();
        using var guarded = ReadOnlyQdrantInterceptor.Connect(fixture.GrpcAddress);

        (await guarded.ListCollectionsAsync()).Should().Contain(collection);

        var hits = await new QdrantKnowledgeIndex(guarded, collection, VectorSize)
            .SearchAsync(new KnowledgeQuery { TextVector = Axis(0), Limit = 5 });

        hits.Should().NotBeEmpty("a search is a read, and reads are what the probe is for");
    }

    [Fact]
    public async Task Guard_RefusesEveryWrite_BeforeItReachesTheIndex()
    {
        var collection = await SeedAsync();
        using var guarded = ReadOnlyQdrantInterceptor.Connect(fixture.GrpcAddress);
        var guardedIndex = new QdrantKnowledgeIndex(guarded, collection, VectorSize);

        await FluentActions.Awaiting(() => guardedIndex.UpsertAsync(
                [new KnowledgePoint(Chunk("intruder", "should never land"), Axis(3))], "run-2"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*read-only*");
        // The Meghalaya chunk was written by run-1, so a sweep for run-2 finds it stale and must try to delete.
        await FluentActions.Awaiting(() => guardedIndex.SweepAsync("plonkit", "india", "run-2"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*read-only*");
        await FluentActions.Awaiting(() => guarded.DeleteCollectionAsync(collection))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*read-only*");

        (await fixture.CreateKnowledgeIndex(collection, VectorSize).CountAsync())
            .Should().Be(3, "nothing the probe tried may have changed the index");
    }

    [Fact]
    public async Task Replay_ShowsWhatRetrievalOffersNow_AgainstWhatTheRatedAnswerWasOffered()
    {
        var collection = await SeedAsync();
        var export = WriteExport(
            "where can I find Khasi pines?",
            offered: ["https://www.plonkit.net/india#khasi", "https://www.plonkit.net/uganda#reindexed-away"],
            cited: ["https://www.plonkit.net/india#khasi"]);

        var report = await RunAsync(collection, "replay", export);

        report.Should().Contain("## 👎 where can I find Khasi pines?");
        report.Should().Contain("> Khasi pines grow in Vietnam too.");
        report.Should().Contain("| 1 | 1 | – | Khasi pines are mainly found in Meghalaya. | [india > Vegetation](https://www.plonkit.net/india#khasi) ✓ |",
            "first by text, absent from the pixel search, and cited by the rated answer");
        report.Should().Contain("Plantations of khasi pines are common in central Vietnam.");
        report.Should().Contain("★", "the Vietnam excerpt was not offered when the answer was rated");
        report.Should().Contain("No longer offered: <https://www.plonkit.net/uganda#reindexed-away>");
        report.Should().Contain("- Answers offered different guides now than when they were rated: 1 of 1");
    }

    [Fact]
    public async Task Grep_FindsEveryChunkThatMentionsIt()
    {
        var collection = await SeedAsync();

        var report = await RunAsync(collection, "grep", "khasi");

        report.Should().Contain("2 of 3 chunks, from 2 guide(s).");
        report.Should().Contain("By country: india 1, vietnam 1");
    }

    [Fact]
    public async Task Compare_CountsEachStepOfTheRetrieval_SideBySide()
    {
        var collection = await SeedAsync();
        var export = WriteExport("where can I find Khasi pines?", offered: [], cited: []);
        var targets = Path.Combine(_workDirectory, "targets.json");
        await File.WriteAllTextAsync(targets, """{ "khasi": { "match": "khasi", "countries": [], "chunks": ["india#khasi"] } }""");

        var report = await RunAsync(collection, "compare", export, "--targets", targets);

        report.Should().Contain("| question | before | +context | +countries | +headings | +planner | +planner, 10 | +planner, 12 | shipped |");
        report.Should().Contain("| **on-target** | **2** of 3 (67 %) |", "both Khasi chunks, of the three the index holds");
        report.Should().Contain("| **target excerpts offered** | **1** of 1 |");
        report.Should().Contain("| where can I find Khasi pines? | `india#khasi` | **1** |");
        report.Should().Contain("The planner was not asked (pass --allow-chat)",
            "without the flag no chat request may leave, and the report says what that means for the numbers");
    }

    [Fact]
    public async Task Compare_TakesVariantsFromAFile_IncludingThoseTheBotFixesInCode()
    {
        var collection = await SeedAsync();
        var export = WriteExport("where can I find Khasi pines?", offered: [], cited: []);
        var variants = Path.Combine(_workDirectory, "variants.json");
        await File.WriteAllTextAsync(variants, """
            { "shipped": {}, "no pixels": { "pixels": 0 }, "approximate": { "approximate": true } }
            """);

        var report = await RunAsync(collection, "compare", export, "--variants", variants);

        report.Should().Contain("| question | shipped | no pixels | approximate |");
        report.Should().Contain("question → pixels ×0");
        report.Should().Contain("approximate search");
        report.Should().Contain("| where can I find Khasi pines? | 3 | 3 | 3 |", "without a target, every excerpt offered is counted");
    }

    [Fact]
    public async Task Replay_LeavesTheGuidesCarriedFromTheAnswerBefore_OutOfTheComparison()
    {
        // The follow-up was offered the Uganda guide its parent cited, first and without a search. Read
        // as retrieval, it showed up as "no longer offered", and the answer as one whose search changed.
        var collection = await SeedAsync();
        var export = WriteExport(
            "and in Vietnam?",
            offered:
            [
                "https://www.plonkit.net/uganda#cited-before", "https://www.plonkit.net/india#khasi",
                "https://www.plonkit.net/vietnam#vietnam", "https://www.plonkit.net/uganda#forest"
            ],
            cited: [],
            before: ("where can I find Khasi pines?", "Mainly Meghalaya [1]."),
            citedBefore: ["https://www.plonkit.net/uganda#cited-before"]);

        var report = await RunAsync(collection, "replay", export);

        report.Should().Contain("1 of them carried from the answer before it rather than searched for.");
        report.Should().NotContain("No longer offered");
        report.Should().Contain("- Answers offered different guides now than when they were rated: 0 of 1");
    }

    [Fact]
    public async Task Replay_SearchesAFollowUpWithItsBranch_AndSaysSo()
    {
        var collection = await SeedAsync();
        var export = WriteExport(
            "and in Vietnam?",
            offered: [],
            cited: [],
            before: ("where can I find Khasi pines?", "Mainly Meghalaya [1]."));

        var report = await RunAsync(collection, "replay", export);

        report.Should().Contain("## 👎 and in Vietnam?");
        report.Should().Contain("_Searched by conversation «where can I find Khasi pines? and in Vietnam?», question «and in Vietnam?» ×0.5");
        report.Should().Contain("within vietnam", "the follow-up names a country the index holds");
    }

    [Fact]
    public async Task Similarity_PlacesAChunkAndAVariantAmongTheQuestionsResults()
    {
        var collection = await SeedAsync();

        var report = await RunAsync(collection, "similarity",
            "--question", "where can I find Khasi pines?",
            "--source", "https://www.plonkit.net/india#khasi",
            "--variant", "Khasi pines grow across Meghalaya.");

        report.Should().Contain("# Similarity to a question");
        report.Should().Contain("| chunk | stored | rank | rebuilt | text |");
        report.Should().Contain("| variant of chunk 1 | similarity | would rank | text |");
    }

    public void Dispose() => Directory.Delete(_workDirectory, recursive: true);

    private async Task<string> RunAsync(string collection, params string[] args)
    {
        using var guarded = ReadOnlyQdrantInterceptor.Connect(fixture.GrpcAddress);
        using var embeddings = new EmbeddingCache(Embedder(), Path.Combine(_workDirectory, "cache.json"));
        var report = new ProbeReport();

        var context = new ProbeContext(
            ProbeArguments.Parse(args)!,
            new ProbeSettings
            {
                QdrantAddress = fixture.GrpcAddress,
                OpenRouter = new OpenRouterConfiguration(),
                Collection = collection
            },
            guarded,
            new QdrantKnowledgeIndex(guarded, collection, VectorSize),
            () => embeddings,
            report,
            CancellationToken.None);

        await (args[0] switch
        {
            "replay" => ReplayCommand.RunAsync(context),
            "compare" => CompareCommand.RunAsync(context),
            "grep" => GrepCommand.RunAsync(context),
            _ => SimilarityCommand.RunAsync(context)
        });

        return report.ToString();
    }

    /// <summary>Three guides: the Meghalaya and Vietnam answers to the Khasi question, and a forest picture.</summary>
    private async Task<string> SeedAsync()
    {
        var collection = QdrantFixture.NewCollectionName();
        var index = fixture.CreateKnowledgeIndex(collection, VectorSize);
        await index.EnsureCollectionAsync();
        await index.UpsertAsync(
        [
            new KnowledgePoint(Chunk("khasi", "Khasi pines are mainly found in Meghalaya.", "india"), Axis(0)),
            new KnowledgePoint(Chunk("vietnam", "Plantations of khasi pines are common in central Vietnam.", "vietnam"),
                Between(0, 1)),
            new KnowledgePoint(
                Chunk("forest", "The final type of coverage is tiny forest paths.", "uganda", "https://img/forest.png"),
                Axis(5), Axis(0))
        ], "run-1");

        return collection;
    }

    /// <param name="before">An earlier question and its answer, making <paramref name="question"/> a follow-up.</param>
    /// <param name="citedBefore">The guides that earlier answer cited, carried into the rated one.</param>
    private string WriteExport(
        string question,
        IReadOnlyList<string> offered,
        IReadOnlyList<string> cited,
        (string Question, string Answer)? before = null,
        IReadOnlyList<string>? citedBefore = null)
    {
        var turns = new List<AiFeedbackTurnRecord>();
        if (before is { } earlier)
        {
            turns.Add(new AiFeedbackTurnRecord(turns.Count, "user", 42, earlier.Question, [], null, DateTimeOffset.UtcNow));
            turns.Add(new AiFeedbackTurnRecord(turns.Count, "assistant", 1, earlier.Answer, [], "test/model", DateTimeOffset.UtcNow)
            {
                CitedChunkIds = citedBefore is null ? null : [Guid.NewGuid()],
                CitedSourceUrls = citedBefore
            });
        }

        turns.Add(new AiFeedbackTurnRecord(turns.Count, "user", 42, question, [], null, DateTimeOffset.UtcNow));
        turns.Add(new AiFeedbackTurnRecord(turns.Count, "assistant", 1, "Mainly Meghalaya [1].", [], "test/model", DateTimeOffset.UtcNow));

        var record = new AiFeedbackRecord(
            Guid.NewGuid(), "negative", "Khasi pines grow in Vietnam too.", 1, 2, 3, 4, 5, "test/model", turns.Count / 2,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            turns,
            offered,
            cited);

        var path = Path.Combine(_workDirectory, "export.jsonl");
        File.WriteAllText(path, AiFeedbackFormatter.RenderJsonLines([record]));
        return path;
    }

    /// <summary>Every question embeds along the first axis, where the Khasi chunks sit.</summary>
    private static IEmbedder Embedder()
    {
        var embedder = Substitute.For<IEmbedder>();
        embedder.ModelId.Returns("test/embedder");
        embedder.EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success(
                [.. ((IReadOnlyList<EmbeddingInput>)call[0]).Select(_ => Axis(0))]));
        return embedder;
    }

    private static KnowledgeChunk Chunk(string key, string text, string country = "tunisia", string? imageUrl = null) =>
        new()
        {
            SourceType = "plonkit",
            SourceKey = country,
            LocalKey = key,
            Kind = imageUrl is null ? KnowledgeChunkKind.Text : KnowledgeChunkKind.Image,
            Text = text,
            SourceUrl = $"https://www.plonkit.net/{country}#{key}",
            ImageUrl = imageUrl,
            Country = country,
            Title = country,
            SectionPath = $"{country} > Vegetation"
        };

    private static ReadOnlyMemory<float> Axis(int index)
    {
        var vector = new float[VectorSize];
        vector[index % VectorSize] = 1f;
        return vector;
    }

    private static ReadOnlyMemory<float> Between(int a, int b)
    {
        var vector = new float[VectorSize];
        vector[a] = vector[b] = (float)(1 / Math.Sqrt(2));
        return vector;
    }
}

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
    public async Task Compare_CountsEveryWeightingSideBySide()
    {
        var collection = await SeedAsync();
        var export = WriteExport("where can I find Khasi pines?", offered: [], cited: []);
        var targets = Path.Combine(_workDirectory, "targets.json");
        await File.WriteAllTextAsync(targets, """{ "khasi": "khasi" }""");

        var report = await RunAsync(collection, "compare", export, "--targets", targets);

        report.Should().Contain("| question | shipped | pixels ×1 | pixels ×0.5 | pixels ×0.25 | pixels ×0 | approximate |");
        report.Should().Contain("| **total** |");
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

    private string WriteExport(string question, IReadOnlyList<string> offered, IReadOnlyList<string> cited)
    {
        var record = new AiFeedbackRecord(
            Guid.NewGuid(), "negative", "Khasi pines grow in Vietnam too.", 1, 2, 3, 4, 5, "test/model", 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [
                new AiFeedbackTurnRecord(0, "user", 42, question, [], null, DateTimeOffset.UtcNow),
                new AiFeedbackTurnRecord(1, "assistant", 1, "Mainly Meghalaya [1].", [], "test/model", DateTimeOffset.UtcNow)
            ],
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

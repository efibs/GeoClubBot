using Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Ingestion;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Drives catalogue sync and ingestion through the real MediatR pipeline against a real database.
/// The extractor, embedder and vector index are faked while the source registry and budget are
/// genuine EF, so what is proven here is the bookkeeping: what gets indexed, what gets retried, and
/// what is deliberately never retried.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class KnowledgeIngestionUseCaseIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Sync_AddsDiscoveredSources_AndRefreshesExistingOnes()
    {
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key, title: "Tunisia"));

        var first = await host.SendAsync(new SyncSourceCatalogsCommand());

        first.IsSuccess.Should().BeTrue();
        first.Value.Added.Should().Be(1);

        // A second sync of the same listing must refresh, not duplicate.
        ArrangeCatalog(host, Descriptor(key, title: "Tunisia (updated)"));
        var second = await host.SendAsync(new SyncSourceCatalogsCommand());

        second.Value.Added.Should().Be(0);
        second.Value.Updated.Should().Be(1);

        var stored = await ReadSourceAsync(key);
        stored!.Title.Should().Be("Tunisia (updated)");
    }

    [Fact]
    public async Task Sync_TombstonesASourceThatIsNoLongerListed()
    {
        await ResetSourcesAsync();

        // A tombstone rather than a delete: an upstream page that vanishes for a day should not
        // silently discard everything indexed from it.
        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        ArrangeCatalog(host);
        var result = await host.SendAsync(new SyncSourceCatalogsCommand());

        result.Value.Tombstoned.Should().Be(1);
        (await ReadSourceAsync(key))!.RemovedFromSyncAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Ingest_IndexesASource_AndRecordsWhatItWrote()
    {
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var index = ArrangeExtraction(host,
            Chunk("a", "Tunisian bollards are short."),
            Chunk("b", "A map of area codes.", imageUrl: "https://i.imgur.com/map.png"));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Ingested.Should().Be(1);
        result.Value.ChunksWritten.Should().Be(2);

        await index.Received().UpsertAsync(
            Arg.Is<IReadOnlyList<KnowledgePoint>>(points =>
                points.Count == 2 && points.Any(point => point.ImageVector != null)),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        var stored = await ReadSourceAsync(key);
        stored!.Status.Should().Be(KnowledgeSourceStatus.Ingested);
        stored.ChunkCount.Should().Be(2);
        stored.ContentHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Ingest_SweepsStalePointsOnlyAfterWriting()
    {
        await ResetSourcesAsync();

        // Deleting before writing would leave the index briefly missing this source, so a question
        // asked mid-ingest would silently get worse answers.
        using var host = CreateHost();
        ArrangeCatalog(host, Descriptor(NewKey()));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        var index = ArrangeExtraction(host, Chunk("a", "text"));

        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        Received.InOrder(() =>
        {
            index.UpsertAsync(Arg.Any<IReadOnlyList<KnowledgePoint>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            index.SweepAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Ingest_SkipsUnchangedContentWithoutEmbeddingItAgain()
    {
        await ResetSourcesAsync();

        using var host = CreateHost();
        ArrangeCatalog(host, Descriptor(NewKey()));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        ArrangeExtraction(host, Chunk("a", "unchanged text"));

        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        var embedder = host.Mock<IEmbedder>();
        embedder.ClearReceivedCalls();

        // A freshly indexed source is not due again for weeks, so age it to reach the unchanged path.
        await BackdateLastAttemptAsync();

        var second = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10, Force: false));

        second.Value.Unchanged.Should().Be(1);
        second.Value.Ingested.Should().Be(0);
        await embedder.DidNotReceive().EmbedAsync(
            Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ingest_MarksAnUningestiblePageSkipped_SoItIsNotRetriedNightly()
    {
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var extractor = ArrangeExtractor(host);
        extractor.ExtractAsync(Arg.Any<SourceDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Failure(
                Error.Validation("ai.not_a_guide_page", "This page carries no guide content.")));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Skipped.Should().Be(1);

        var stored = await ReadSourceAsync(key);
        stored!.Status.Should().Be(KnowledgeSourceStatus.Skipped);
        stored.IsDueForIngest(DateTimeOffset.UtcNow.AddYears(1), TimeSpan.FromDays(1))
            .Should().BeFalse("a skipped source must never re-enter the queue");
    }

    [Fact]
    public async Task Ingest_MarksATransientFailureForRetryWithBackoff()
    {
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var extractor = ArrangeExtractor(host);
        extractor.ExtractAsync(Arg.Any<SourceDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Failure(
                Error.Unexpected("ai.source_unreachable", "Could not fetch the page.")));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Failed.Should().Be(1);

        var stored = await ReadSourceAsync(key);
        stored!.Status.Should().Be(KnowledgeSourceStatus.Failed);
        stored.ConsecutiveFailures.Should().Be(1);
        stored.IsDueForIngest(DateTimeOffset.UtcNow, TimeSpan.FromDays(14))
            .Should().BeFalse("backoff keeps a broken source from consuming every run");
    }

    [Fact]
    public async Task Ingest_IndexesTextOnly_WhenImagesCannotBeEmbedded()
    {
        await ResetSourcesAsync();

        // Several guide sites block unattended image fetches, and the embedding provider fetches
        // server-side, so one blocked host must cost image search rather than the whole source.
        using var host = CreateHost();
        ArrangeCatalog(host, Descriptor(NewKey()));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var index = ArrangeExtraction(
            host,
            imagesFail: true,
            Chunk("a", "caption", imageUrl: "https://blocked.example/x.png"));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Ingested.Should().Be(1, "the source is still worth indexing without its images");

        var written = index.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IKnowledgeIndex.UpsertAsync))
            .Select(call => (IReadOnlyList<KnowledgePoint>)call.GetArguments()[0]!)
            .Single();

        written.Should().OnlyContain(point => point.ImageVector == null,
            "a blocked image host must cost image search for that source, not the source itself");
        written.Should().OnlyContain(point => point.Chunk.ImageUrl != null,
            "the chunk still records where its image lives, so it can be shown and retried later");
    }

    [Fact]
    public async Task Ingest_StopsEarly_WhenTheDailyAllowanceIsSpent()
    {
        await ResetSourcesAsync();
        await ResetTodaysBudgetAsync();

        using var host = CreateHost(dailyBudget: 1);
        ArrangeCatalog(host, Descriptor(NewKey()), Descriptor(NewKey()));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        ArrangeExtraction(host, Chunk("a", "text"));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.BudgetExhausted.Should().BeTrue();
        result.Value.Ingested.Should().BeLessThan(2, "the run must stop rather than earn a stream of 429s");
    }

    /// <summary>
    /// Empties the source registry. The ingestion queue is deliberately global — a run picks up
    /// whatever is due — so tests sharing a container must start from an empty queue or they see
    /// each other's sources.
    /// </summary>
    [Fact]
    public async Task Ingest_ResumesAcrossRuns_UntilEveryUnindexedSourceIsCovered()
    {
        // The backfill story: with a small daily allowance the library is indexed over several runs,
        // so a run that stops early must leave the untouched sources at the front of the queue.
        await ResetSourcesAsync();
        await ResetTodaysBudgetAsync();

        // One text-only source costs a single embedding request, so an allowance of one covers one.
        using var host = CreateHost(dailyBudget: 1);
        ArrangeCatalog(host, Descriptor(NewKey()), Descriptor(NewKey()), Descriptor(NewKey()));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        ArrangeExtraction(host, Chunk("a", "text"));

        var first = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));
        first.Value.Ingested.Should().Be(1, "the allowance covers exactly one source");
        first.Value.BudgetExhausted.Should().BeTrue();

        // A new day resets the allowance; the two untouched sources are still first in line because
        // an out-of-budget source has its state left alone.
        await ResetTodaysBudgetAsync();

        var second = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));
        second.Value.Ingested.Should().Be(1);

        await ResetTodaysBudgetAsync();
        var third = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));
        third.Value.Ingested.Should().Be(1);

        await ResetTodaysBudgetAsync();
        var fourth = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));
        fourth.Value.Ingested.Should().Be(0, "everything is indexed and nothing is due again yet");
    }

    [Fact]
    public async Task Ingest_RetriesImages_WhenAnEarlierRunCouldOnlyAffordTheText()
    {
        // A run can afford a source's text and then run out before its images. The source must not be
        // recorded as fully indexed, or its images are lost until someone forces a rebuild.
        await ResetSourcesAsync();
        await ResetTodaysBudgetAsync();

        // An allowance of one covers the text and leaves nothing for the image.
        using (var lean = CreateHost(dailyBudget: 1))
        {
            ArrangeCatalog(lean, Descriptor(NewKey()));
            await lean.SendAsync(new SyncSourceCatalogsCommand());
            ArrangeExtraction(lean, Chunk("a", "caption", imageUrl: "https://i.imgur.com/a.png"));

            await lean.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));
        }

        // A later run with room to spare must pick the source back up, without waiting out the
        // normal re-ingest interval and without anyone forcing a rebuild.
        await ResetTodaysBudgetAsync();

        using var host = CreateHost(dailyBudget: 100);
        var index = ArrangeExtraction(host, Chunk("a", "caption", imageUrl: "https://i.imgur.com/a.png"));

        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        var written = index.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IKnowledgeIndex.UpsertAsync))
            .Select(call => (IReadOnlyList<KnowledgePoint>)call.GetArguments()[0]!)
            .LastOrDefault();

        written.Should().NotBeNull("the source must be re-indexed once the allowance allows its images");
        written.Should().OnlyContain(point => point.ImageVector != null);
    }

    private async Task ResetSourcesAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "KnowledgeSources" """);
    }

    private async Task BackdateLastAttemptAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE "KnowledgeSources" SET "LastAttemptedAtUtc" = now() - interval '400 days' """);
    }

    private async Task ResetTodaysBudgetAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "AiDailyBudgets" WHERE "DateUtc" = {DateOnly.FromDateTime(DateTime.UtcNow)}""");
    }

    private async Task<KnowledgeSource?> ReadSourceAsync(string naturalKey)
    {
        await using var db = fixture.CreateDbContext();
        return await new Infrastructure.OutputAdapters.Repositories.EfKnowledgeSourceRepository(db)
            .ReadAsync("plonkit", naturalKey);
    }

    private MediatorTestHost CreateHost(int dailyBudget = 1000) =>
        new(fixture.ConnectionString, configurationValues: new Dictionary<string, string?>
        {
            ["AI:Active"] = "true",
            ["AI:OpenRouter:DailyRequestBudget"] = dailyBudget.ToString(),
            ["AI:OpenRouter:EmbeddingBatchSize"] = "32",
            ["AI:Ingestion:MaxSourcesPerRun"] = "25"
        });

    private static void ArrangeCatalog(MediatorTestHost host, params SourceDescriptor[] descriptors)
    {
        var catalog = host.Mock<ISourceCatalog>();

        // The handler reads existing sources by the catalog's own type; leaving it unstubbed would
        // look up type "" and treat every already-known source as new.
        catalog.SourceType.Returns("plonkit");
        catalog.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SourceDescriptor>>.Success(descriptors));
    }

    private static ISourceExtractor ArrangeExtractor(MediatorTestHost host)
    {
        var extractor = Substitute.For<ISourceExtractor>();
        extractor.SourceType.Returns("plonkit");

        host.Mock<ISourceExtractorRegistry>().ResolveByType("plonkit").Returns(extractor);
        return extractor;
    }

    private static IKnowledgeIndex ArrangeExtraction(MediatorTestHost host, params ExtractedChunk[] chunks) =>
        ArrangeExtraction(host, imagesFail: false, chunks);

    /// <summary>
    /// Scripts a successful extraction plus the embedder and index it feeds. Image failure is decided
    /// inside a single callback rather than by a second overlapping argument matcher, so which stub
    /// answers a given call is unambiguous.
    /// </summary>
    private static IKnowledgeIndex ArrangeExtraction(
        MediatorTestHost host,
        bool imagesFail,
        params ExtractedChunk[] chunks)
    {
        ArrangeExtractor(host)
            .ExtractAsync(Arg.Any<SourceDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Success(new ExtractedDocument("Tunisia", null, chunks)));

        host.Mock<IEmbedder>().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var inputs = (IReadOnlyList<EmbeddingInput>)call[0];

                return imagesFail && inputs.Any(input => input is ImageEmbeddingInput)
                    ? Result<IReadOnlyList<ReadOnlyMemory<float>>>.Failure(
                        Error.Unexpected("ai.embedding_failed", "403 fetching image"))
                    : Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success(
                        [.. inputs.Select(_ => new ReadOnlyMemory<float>(new float[4]))]);
            });

        return host.Mock<IKnowledgeIndex>();
    }

    private static ExtractedChunk Chunk(string key, string text, string? imageUrl = null) =>
        new(key, "Tunisia > Identifying", text, imageUrl);

    private static SourceDescriptor Descriptor(string naturalKey, string? title = null) =>
        new("plonkit", naturalKey, new Uri($"https://www.plonkit.net/{naturalKey}"), title, Country: naturalKey);

    /// <summary>Unique per test so the shared container needs no cleanup between them.</summary>
    private static string NewKey() => $"country-{Guid.NewGuid():N}";
}

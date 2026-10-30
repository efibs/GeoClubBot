using Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
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
    public async Task Sync_AddsAResourceOnce_WhenTwoCataloguesBothListIt()
    {
        // The guide site publishes its own index, and the community library links to the same guides.
        // Both catalogues therefore yield the identical source, and inserting it twice violates the
        // unique index on (SourceType, NaturalKey) -- which is exactly what a real sync hit.
        await ResetSourcesAsync();

        var shared = Descriptor(NewKey());

        using var host = CreateHostWithCatalogs(
            ("plonkit", [shared]),
            ("meta-library", [shared, Descriptor(NewKey())]));

        var result = await host.SendAsync(new SyncSourceCatalogsCommand());

        result.IsSuccess.Should().BeTrue();
        result.Value.Added.Should().Be(2, "the shared resource counts once");
        result.Value.Discovered.Should().Be(2);
    }

    [Fact]
    public async Task Sync_RecognisesExistingSources_FromACatalogueThatYieldsSeveralTypes()
    {
        // A library catalogue advertises its own name but produces guides, documents and albums.
        // Matching existing sources by the catalogue's type instead of each descriptor's type found
        // nothing, so every sync re-added everything.
        await ResetSourcesAsync();

        var guide = Descriptor(NewKey());
        var document = new SourceDescriptor("gdoc", NewKey(), new Uri("https://docs.google.com/document/d/x/edit"));

        using (var first = CreateHostWithCatalogs(("meta-library", [guide, document])))
        {
            (await first.SendAsync(new SyncSourceCatalogsCommand())).Value.Added.Should().Be(2);
        }

        using var second = CreateHostWithCatalogs(("meta-library", [guide, document]));
        var result = await second.SendAsync(new SyncSourceCatalogsCommand());

        result.Value.Added.Should().Be(0, "a second sync of the same listing must update, not insert");
        result.Value.Updated.Should().Be(2);
    }

    [Fact]
    public async Task Sync_LeavesOtherCataloguesAlone_WhenOneOfThemFails()
    {
        // A catalogue that could not be read has not stopped publishing anything, so its sources must
        // not be tombstoned as though it had dropped them.
        await ResetSourcesAsync();

        var guide = Descriptor(NewKey());
        using (var seed = CreateHostWithCatalogs(("plonkit", [guide])))
        {
            await seed.SendAsync(new SyncSourceCatalogsCommand());
        }

        // This run has no working plonkit catalogue at all: the one that would list that guide failed.
        using var host = new MediatorTestHost(fixture.ConnectionString,
            configure: services =>
            {
                var failing = Substitute.For<ISourceCatalog>();
                failing.SourceType.Returns("plonkit");
                failing.ListAsync(Arg.Any<CancellationToken>())
                    .Returns(Result<IReadOnlyList<SourceDescriptor>>.Failure(
                        Error.Unexpected("ai.library_unreachable", "down")));

                services.AddSingleton(failing);
            },
            configurationValues: new Dictionary<string, string?> { ["AI:Active"] = "true" });

        var result = await host.SendAsync(new SyncSourceCatalogsCommand());

        result.Value.Tombstoned.Should().Be(0);
        (await ReadSourceAsync(guide.NaturalKey))!.RemovedFromSyncAtUtc.Should().BeNull();
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
    public async Task Ingest_IndexesWithoutAnImageTheProviderRejects_AndDoesNotRetryIt()
    {
        await ResetSourcesAsync();

        // Several guide sites block unattended image fetches, so a rejected picture must cost itself, not
        // the source. And a rejection recurs, so retrying it would spend the allowance every run.
        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var index = ArrangeExtraction(
            host,
            WhenEmbeddingImages(_ => Rejected("Received 403 status code when fetching image")),
            Chunk("a", "caption", imageUrl: "https://blocked.example/x.png"));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Ingested.Should().Be(1, "the source is still worth indexing without its image");

        var written = LastWrite(index);
        written.Should().OnlyContain(point => point.ImageVector == null);
        written.Should().OnlyContain(point => point.Chunk.ImageUrl != null,
            "the chunk still records where its image lives, so it can be shown");

        (await ReadSourceAsync(key))!.ImagesDeferred.Should().BeFalse();
    }

    [Fact]
    public async Task Ingest_ComesBackForImages_WhenTheProviderFailedOnItsSide()
    {
        // An outage or a timeout is about the moment, not the pictures. Recording the source as fully
        // indexed is what lost images for good: unchanged content is never embedded again.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        ArrangeExtraction(
            host,
            WhenEmbeddingImages(_ => Unavailable("The embedding provider failed (HTTP 502)")),
            Chunk("a", "caption", imageUrl: "https://i.imgur.com/a.png"));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Ingested.Should().Be(1);

        var stored = await ReadSourceAsync(key);
        stored!.ImagesDeferred.Should().BeTrue();
        stored.IsDueForIngest(DateTimeOffset.UtcNow, TimeSpan.FromDays(14))
            .Should().BeTrue("the source goes straight back in the queue for its pictures");
    }

    [Fact]
    public async Task Ingest_KeepsTheOtherBatches_WhenOneBatchOfImagesFails()
    {
        // Production lost 164 images at once: the first failed batch ended image embedding for the whole
        // source. Two images per batch here, and the batch holding "a" fails; "c" and "d" must survive it.
        await ResetSourcesAsync();

        using var host = CreateHost(batchSize: 2);
        ArrangeCatalog(host, Descriptor(NewKey()));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var index = ArrangeExtraction(
            host,
            WhenEmbeddingImages(images => images.Any(image => image.ImageUrl.EndsWith("/a.png", StringComparison.Ordinal))
                ? Unavailable("timed out")
                : EmbedEverything(images)),
            ImageChunks("a", "b", "c", "d"));

        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        var written = LastWrite(index);
        written.Where(point => point.ImageVector != null).Select(point => point.Chunk.LocalKey)
            .Should().BeEquivalentTo("c", "d");
    }

    [Fact]
    public async Task Ingest_NarrowsARejectedBatchDownToTheImageAtFault()
    {
        // One dead picture used to cost every image batched with it, and on every run. Halving the batch
        // until the rejection is isolated costs a few extra requests once, and the rest are indexed.
        await ResetSourcesAsync();

        using var host = CreateHost(batchSize: 4);
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var index = ArrangeExtraction(
            host,
            WhenEmbeddingImages(images => images.Any(image => image.ImageUrl.EndsWith("/c.png", StringComparison.Ordinal))
                ? Rejected("Received 404 status code when fetching image")
                : EmbedEverything(images)),
            ImageChunks("a", "b", "c", "d"));

        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        LastWrite(index).Where(point => point.ImageVector != null).Select(point => point.Chunk.LocalKey)
            .Should().BeEquivalentTo("a", "b", "d");

        ImageCalls(host).Should().HaveCount(5, "all four, then each half, then each image of the rejected half");
        (await ReadSourceAsync(key))!.ImagesDeferred.Should().BeFalse("the image at fault would be rejected again");
    }

    [Fact]
    public async Task Ingest_DropsOnlyTheImageTheProviderNames_RatherThanHalvingTheBatch()
    {
        // The provider names the URL it refused. Halving costs a request per level, and every one of
        // them comes out of the same daily allowance the guides are being indexed with: dropping what
        // was named and re-sending the rest settles a batch of four in two requests instead of five.
        await ResetSourcesAsync();

        using var host = CreateHost(batchSize: 4);
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var index = ArrangeExtraction(
            host,
            WhenEmbeddingImages(images => images.Any(image => image.ImageUrl.EndsWith("/c.png", StringComparison.Ordinal))
                ? Rejected("The embedding provider rejected the request (HTTP 415): URL did not return an "
                           + "image (received text/error content): https://i.imgur.com/c.png")
                : EmbedEverything(images)),
            ImageChunks("a", "b", "c", "d"));

        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        LastWrite(index).Where(point => point.ImageVector != null).Select(point => point.Chunk.LocalKey)
            .Should().BeEquivalentTo("a", "b", "d");

        ImageCalls(host).Should().HaveCount(2, "all four, then the three the provider did not name");
        (await ReadSourceAsync(key))!.ImagesDeferred.Should().BeFalse("the image at fault would be rejected again");
    }

    [Fact]
    public async Task Ingest_IgnoresAMessageNamingEveryImage_AndNarrowsAsUsual()
    {
        // A message naming every image in the request says the request was refused, not that each
        // picture is broken. Read as names, it would cost a source all of its images at once.
        await ResetSourcesAsync();

        using var host = CreateHost(batchSize: 2);
        ArrangeCatalog(host, Descriptor(NewKey()));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var index = ArrangeExtraction(
            host,
            WhenEmbeddingImages(images => images.Count > 1
                ? Rejected($"too many images: {string.Join(", ", images.Select(image => image.ImageUrl))}")
                : EmbedEverything(images)),
            ImageChunks("a", "b"));

        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        LastWrite(index).Where(point => point.ImageVector != null).Select(point => point.Chunk.LocalKey)
            .Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task Ingest_IndexesAChunkWithoutItsPicture_WhenTheFormatCannotBeEmbedded()
    {
        // A guide's vector illustrations are refused by the provider, and a refusal fails the whole
        // request they travelled in. Sending them at all cost the images batched alongside them, then
        // the requests spent narrowing the batch down — every run, since the rejection recurs.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var index = ArrangeExtraction(
            host,
            Chunk("a", "Balinese shrines", imageUrl: "https://rmrg.me/guides/id/vectors/hinduism-2.svgz?v=8"),
            Chunk("b", "A map of area codes.", imageUrl: "https://i.imgur.com/map.png"));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Ingested.Should().Be(1);

        var written = LastWrite(index);
        written.Single(point => point.Chunk.LocalKey == "a").Chunk.ImageUrl
            .Should().BeNull("a picture nothing downstream can render is not worth citing either");
        written.Single(point => point.Chunk.LocalKey == "b").ImageVector.Should().NotBeNull();

        ImageCalls(host).SelectMany(images => images).Should().OnlyContain(image => image.ImageUrl.EndsWith(".png"),
            "the unusable format never reaches the provider");

        (await ReadSourceAsync(key))!.ImagesDeferred.Should().BeFalse("nothing is owed for a later run");
    }

    [Fact]
    public async Task Ingest_StopsNarrowing_WhenTheProviderRejectsImagesInGeneral()
    {
        // Every image rejected reads as the provider refusing images, not as sixteen broken pictures.
        // Narrowing that down one request at a time would take thirty-one requests to learn nothing.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        ArrangeExtraction(
            host,
            WhenEmbeddingImages(_ => Rejected("image input is not supported")),
            ImageChunks([.. Enumerable.Range(0, 16).Select(index => $"i{index}")]));

        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        ImageCalls(host).Count.Should().BeLessThan(31);
        (await ReadSourceAsync(key))!.ImagesDeferred
            .Should().BeTrue("a refusal of images in general may pass, so the source comes back for them");
    }

    [Fact]
    public async Task Ingest_StopsTheRun_WithoutFailingAnySource_WhenStillRateLimited()
    {
        // The request layer has already waited out the provider's window once. Recording what follows as
        // the source's failure backed off good sources one after another, as each met the same spent window.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var first = NewKey();
        var second = NewKey();
        ArrangeCatalog(host, Descriptor(first), Descriptor(second));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        ArrangeExtraction(host, _ => RateLimited(), Chunk("a", "text"));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.RateLimited.Should().BeTrue();
        result.Value.Failed.Should().Be(0);
        result.Value.Attempted.Should().Be(0);

        foreach (var key in (string[])[first, second])
        {
            var stored = await ReadSourceAsync(key);
            stored!.Status.Should().Be(KnowledgeSourceStatus.Pending);
            stored.ConsecutiveFailures.Should().Be(0);
            stored.LastAttemptedAtUtc.Should().BeNull("an untouched source keeps its place at the front of the queue");
        }

        host.Mock<IEmbedder>().ReceivedCalls().Should().ContainSingle("the run stops at the first refusal");
    }

    [Fact]
    public async Task Ingest_KeepsTheText_AndStopsTheRun_WhenImagesAreStillRateLimited()
    {
        await ResetSourcesAsync();

        using var host = CreateHost();
        var first = NewKey();
        var second = NewKey();
        ArrangeCatalog(host, Descriptor(first), Descriptor(second));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        ArrangeExtraction(
            host,
            WhenEmbeddingImages(_ => RateLimited()),
            Chunk("a", "caption", imageUrl: "https://i.imgur.com/a.png"));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.RateLimited.Should().BeTrue();
        result.Value.Ingested.Should().Be(1);

        var stored = new[] { await ReadSourceAsync(first), await ReadSourceAsync(second) };
        stored.Should().ContainSingle(source => source!.Status == KnowledgeSourceStatus.Ingested && source.ImagesDeferred);
        stored.Should().ContainSingle(source => source!.Status == KnowledgeSourceStatus.Pending);
    }

    [Fact]
    public async Task Ingest_SendsRelayedImagesInline_SoTheProviderNeverFetchesThisHost()
    {
        // The nightly failure: the provider fetching relayed images back through the bot's tunnel got a
        // 525. The relay already holds the bytes, so they travel with the request instead.
        await ResetSourcesAsync();

        const string relayed = "https://host.tailnet.ts.net/api/v1/ai/images/abc.png";
        const string inline = "data:image/png;base64,AAAA";

        using var host = CreateHost();
        ArrangeCatalog(host, Descriptor(NewKey()));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        host.Mock<IImageRelay>().ReadAsDataUrlAsync(relayed, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(inline));

        var index = ArrangeExtraction(host, Chunk("a", "caption", imageUrl: relayed));

        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        ImageCalls(host).Should().ContainSingle().Which.Should().ContainSingle()
            .Which.ImageUrl.Should().Be(inline);

        var point = LastWrite(index).Single();
        point.Chunk.ImageUrl.Should().Be(relayed, "the index keeps the public URL, which is what Discord shows");
        point.ImageVector.Should().NotBeNull();
    }

    [Fact]
    public async Task Ingest_SplitsAnImageBatch_ThatWouldExceedTheRequestSizeLimit()
    {
        // Inline pictures make a batch's size depend on the pictures, not the count. Three 60-byte images
        // under a 100-byte limit cannot share a request.
        await ResetSourcesAsync();

        using var host = CreateHost(maxRequestBytes: 100);
        ArrangeCatalog(host, Descriptor(NewKey()));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        host.Mock<IImageRelay>().ReadAsDataUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("data:image/png;base64," + new string('A', 38)));

        ArrangeExtraction(host, ImageChunks("a", "b", "c"));

        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        ImageCalls(host).Should().HaveCount(3).And.OnlyContain(images => images.Count == 1);
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

    [Fact]
    public async Task Ingest_StopsAtItsShare_SoQuestionsStillWork()
    {
        // The reason the ceiling exists: indexing runs overnight and draws on the same daily counter
        // as answering, so without it a backfill would leave the bot mute for the rest of the day.
        await ResetSourcesAsync();
        await ResetTodaysBudgetAsync();

        // Ten requests a day at a 60% share: indexing may claim six, leaving four — enough for two
        // questions, which cost two requests each.
        using var host = CreateHost(dailyBudget: 10, ingestionPercent: 60);
        ArrangeCatalog(host, [.. Enumerable.Range(0, 10).Select(_ => Descriptor(NewKey()))]);
        await host.SendAsync(new SyncSourceCatalogsCommand());
        ArrangeExtraction(host, Chunk("a", "text"));

        var ingest = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 50));

        ingest.Value.Ingested.Should().Be(6, "indexing stops at its share rather than the full allowance");
        ingest.Value.BudgetExhausted.Should().BeTrue();

        ArrangeChat(host);
        var answer = await host.SendAsync(new AskAiCommand(
            NewSnowflake(), null, ChannelId: 5, GuildId: 7, NewSnowflake(), "what about bollards?", []));

        answer.IsSuccess.Should().BeTrue("the allowance reserved for questions must still be available");
    }

    [Fact]
    public async Task Ingest_CanUseTheWholeAllowance_WhenTheShareIsUnrestricted()
    {
        await ResetSourcesAsync();
        await ResetTodaysBudgetAsync();

        using var host = CreateHost(dailyBudget: 4, ingestionPercent: 100);
        ArrangeCatalog(host, [.. Enumerable.Range(0, 10).Select(_ => Descriptor(NewKey()))]);
        await host.SendAsync(new SyncSourceCatalogsCommand());
        ArrangeExtraction(host, Chunk("a", "text"));

        var ingest = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 50));

        ingest.Value.Ingested.Should().Be(4, "an unrestricted share lets indexing spend everything");
    }

    [Fact]
    public async Task Backfill_QueuesIndexedSourcesWhoseImagesNeverMadeItIn()
    {
        // The damage already done: a run that lost a source's images still recorded it as fully indexed,
        // and unchanged content is never embedded again, so those pictures stayed missing indefinitely.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var damaged = NewKey();
        var intact = NewKey();
        ArrangeCatalog(host, Descriptor(damaged), Descriptor(intact));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        var index = ArrangeExtraction(host, Chunk("a", "text"));
        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        // Listed by the index but not yet indexed here, and listed but unknown to the registry at all.
        var pending = NewKey();
        ArrangeCatalog(host, Descriptor(damaged), Descriptor(intact), Descriptor(pending));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        index.ReadSourcesMissingImageVectorsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IndexedSourceKey>>(
            [
                new IndexedSourceKey("plonkit", damaged),
                new IndexedSourceKey("plonkit", pending),
                new IndexedSourceKey("plonkit", NewKey())
            ]));

        var result = await host.SendAsync(new BackfillMissingImagesCommand());

        result.IsSuccess.Should().BeTrue();
        result.Value.SourcesWithMissingImages.Should().Be(3);
        result.Value.Queued.Should().Be(1, "only an indexed source can be missing images; the rest are due anyway or unknown");

        var queued = await ReadSourceAsync(damaged);
        queued!.ImagesDeferred.Should().BeTrue();
        queued.IsDueForIngest(DateTimeOffset.UtcNow, TimeSpan.FromDays(14)).Should().BeTrue();
        (await ReadSourceAsync(intact))!.ImagesDeferred.Should().BeFalse();

        // And the next run really re-embeds it, rather than finding its content unchanged and moving on.
        var rerun = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));
        rerun.Value.Unchanged.Should().Be(0);
        (await ReadSourceAsync(damaged))!.LastIngestedAtUtc.Should().BeAfter(queued.LastIngestedAtUtc!.Value);
    }

    [Fact]
    public async Task Backfill_Refuses_WhenImageEmbeddingIsTurnedOff()
    {
        // Queued sources would only be re-indexed text-only again, spending requests to change nothing.
        using var host = CreateHost(embedImages: false);

        var result = await host.SendAsync(new BackfillMissingImagesCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.image_embedding_disabled");
        await host.Mock<IKnowledgeIndex>().DidNotReceive()
            .ReadSourcesMissingImageVectorsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ingest_MarksADeletedSourceUnavailable_AndLeavesItAloneUntilTheRecheck()
    {
        // Production's nine dead links answered 410 every night, were recorded as failed, and took a slot
        // of every third run for ever.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        ArrangeFailure(host, Error.NotFound(SourceExtractionErrorCodes.Gone, "This document no longer exists (HTTP 410)."));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Gone.Should().Be(1);
        result.Value.Failed.Should().Be(0);

        var stored = await ReadSourceAsync(key);
        stored!.Status.Should().Be(KnowledgeSourceStatus.Unavailable);
        stored.StatusReason.Should().Contain("410");

        // Not on the failure backoff's next night: on its own, far longer interval.
        await BackdateLastAttemptAsync(TimeSpan.FromDays(3));
        (await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10))).Value.Attempted.Should().Be(0);

        await BackdateLastAttemptAsync(TimeSpan.FromDays(31));
        (await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10))).Value.Gone.Should().Be(1);
    }

    [Fact]
    public async Task Ingest_RemovesADeletedSourcesChunks_OnlyWhenTheRecheckFindsItStillGone()
    {
        // Nothing stopped a deleted guide from being cited for ever. One check is not enough to drop it —
        // a guide briefly unshared would lose everything — so the second one in a row does.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        var index = ArrangeExtraction(host, Chunk("a", "Tunisian bollards are short."));
        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        await BackdateLastAttemptAsync();
        index.ClearReceivedCalls();
        ArrangeFailure(host, Error.NotFound(SourceExtractionErrorCodes.Gone, "This guide page no longer exists (HTTP 404)."));

        var first = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        first.Value.Removed.Should().Be(0);
        await index.DidNotReceive().SweepAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await ReadSourceAsync(key))!.HoldsIndexedContent.Should().BeTrue();

        await BackdateLastAttemptAsync(TimeSpan.FromDays(31));
        var second = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        second.Value.Removed.Should().Be(1);
        await index.Received(1).SweepAsync("plonkit", key, Arg.Any<string>(), Arg.Any<CancellationToken>());

        var stored = await ReadSourceAsync(key);
        stored!.HoldsIndexedContent.Should().BeFalse();
        stored.ContentHash.Should().BeNull("a guide that comes back must be written in full, not judged unchanged");
    }

    [Fact]
    public async Task Ingest_KeepsAPrivateSourcesChunks_HoweverOftenItIsFoundPrivate()
    {
        // A private document still exists and may be shared again; its owner unsharing it for a while is
        // no reason to forget what it said.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        var index = ArrangeExtraction(host, Chunk("a", "Tunisian bollards are short."));
        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));
        index.ClearReceivedCalls();

        ArrangeFailure(host, Error.Forbidden(SourceExtractionErrorCodes.NotPublic, "This document is not publicly readable (HTTP 403)."));

        for (var check = 0; check < 3; check++)
        {
            await BackdateLastAttemptAsync(TimeSpan.FromDays(400));
            (await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10))).Value.NotPublic.Should().Be(1);
        }

        await index.DidNotReceive().SweepAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await ReadSourceAsync(key))!.HoldsIndexedContent.Should().BeTrue();
    }

    [Fact]
    public async Task Ingest_BringsBackAnUnavailableSource_WhenItIsSharedAgain()
    {
        // Skipped, as it used to be, a document shared again never came back.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        ArrangeFailure(host, Error.Forbidden(SourceExtractionErrorCodes.NotPublic, "This document is not publicly readable (HTTP 403)."));
        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        await BackdateLastAttemptAsync(TimeSpan.FromDays(31));
        ArrangeExtraction(host, Chunk("a", "Tunisian bollards are short."));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Ingested.Should().Be(1);
        var stored = await ReadSourceAsync(key);
        stored!.Status.Should().Be(KnowledgeSourceStatus.Ingested);
        stored.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task Ingest_LeavesEverySourceOfARefusingHostUntouched_AndCarriesOnWithOtherHosts()
    {
        // One open circuit used to be recorded as the failure of every source after it, other sites'
        // included, and back each of them off. A refusal says nothing about the guides themselves.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var refused = new[] { NewKey(), NewKey() };
        var elsewhere = NewKey();
        ArrangeCatalog(host,
            Descriptor(refused[0]), Descriptor(refused[1]),
            new SourceDescriptor("plonkit", elsewhere, new Uri($"https://mirror.example/{elsewhere}"), Country: elsewhere));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var extractor = ArrangeExtractor(host);
        extractor.ExtractAsync(Arg.Is<SourceDescriptor>(source => source.Url.Host == "www.plonkit.net"), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Failure(
                Error.Conflict(SourceExtractionErrorCodes.HostRefused, "The host of this guide page is refusing requests (HTTP 429).")));
        extractor.ExtractAsync(Arg.Is<SourceDescriptor>(source => source.Url.Host == "mirror.example"), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Success(new ExtractedDocument("Tunisia", null, [Chunk("a", "text")])));
        host.Mock<IEmbedder>().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => EmbedEverything((IReadOnlyList<EmbeddingInput>)call[0]));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.HostRefused.Should().Be(2);
        result.Value.Ingested.Should().Be(1, "another site's guides are no business of the refusing host");
        result.Value.Failed.Should().Be(0);
        await extractor.Received(1).ExtractAsync(
            Arg.Is<SourceDescriptor>(source => source.Url.Host == "www.plonkit.net"), Arg.Any<CancellationToken>());

        foreach (var key in refused)
        {
            var stored = await ReadSourceAsync(key);
            stored!.Status.Should().Be(KnowledgeSourceStatus.Pending);
            stored.LastAttemptedAtUtc.Should().BeNull("it keeps its place at the front of the next run's queue");
        }
    }

    [Fact]
    public async Task Ingest_GivesTheSlotsOfARefusingHostsSources_ToTheNextSourcesOfOtherHosts()
    {
        // Handed a fixed batch, a run whose batch was all one refusing host's sources did nothing else, night
        // after night while the refusal lasted, with every other site's due guides waiting behind them.
        await ResetSourcesAsync();

        var now = DateTimeOffset.UtcNow;
        var refused = new[] { SourceOn("www.plonkit.net"), SourceOn("www.plonkit.net") };
        var elsewhere = new[] { SourceOn("mirror.example"), SourceOn("mirror.example") };

        // Never indexed sorts first, so the refusing host's sources take the whole of a two-slot batch.
        foreach (var source in elsewhere)
        {
            source.MarkIngested("hash", null, chunkCount: 1, imageCount: 0, now.AddDays(-20));
        }

        await SeedAsync([.. refused, .. elsewhere]);

        using var host = CreateHost();
        var extractor = ArrangeExtractor(host);
        extractor.ExtractAsync(Arg.Is<SourceDescriptor>(source => source.Url.Host == "www.plonkit.net"), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Failure(
                Error.Conflict(SourceExtractionErrorCodes.HostRefused, "The host of this guide page is refusing requests (HTTP 429).")));
        extractor.ExtractAsync(Arg.Is<SourceDescriptor>(source => source.Url.Host == "mirror.example"), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Success(new ExtractedDocument("Tunisia", null, [Chunk("a", "new text")])));
        host.Mock<IEmbedder>().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => EmbedEverything((IReadOnlyList<EmbeddingInput>)call[0]));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 2));

        result.Value.HostRefused.Should().Be(2);
        result.Value.Ingested.Should().Be(1, "the refused source's slot goes to another host's source, and only the one");
        extractor.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ISourceExtractor.ExtractAsync))
            .Should().Be(2, "the limit still bounds what a run asks of the guide hosts");

        foreach (var source in refused)
        {
            (await ReadSourceAsync(source.NaturalKey))!.LastAttemptedAtUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task Ingest_KeepsTheIndexedPictures_WhenOnlyADegradedTextExportCouldBeRead()
    {
        // A document's archive that timed out tonight will probably answer tomorrow. Writing its text in the
        // meantime would sweep every picture and spend the allowance embedding them all again.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        var index = ArrangeExtraction(host, Chunk("a", "caption", imageUrl: "https://i.imgur.com/a.png"));
        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));
        var indexed = await ReadSourceAsync(key);

        await BackdateLastAttemptAsync();
        index.ClearReceivedCalls();
        ArrangeExtractor(host).ExtractAsync(Arg.Any<SourceDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Success(
                new ExtractedDocument("Tunisia", null, [Chunk("p0", "caption")], Degraded: true)));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Failed.Should().Be(1);
        await index.DidNotReceive().UpsertAsync(
            Arg.Any<IReadOnlyList<KnowledgePoint>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        var stored = await ReadSourceAsync(key);
        stored!.ImageCount.Should().Be(indexed!.ImageCount).And.BeGreaterThan(0);
        stored.ContentHash.Should().Be(indexed.ContentHash);
    }

    [Fact]
    public async Task Ingest_KeepsPicturesStillOwedTheirEmbedding_WhenOnlyADegradedTextExportCouldBeRead()
    {
        // Every picture postponed leaves an image count of zero, yet their chunks are written and owed a
        // retry. Judged by the count, the degraded text replaced them and cleared the debt, leaving the
        // source without pictures until its next re-index weeks later.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        var index = ArrangeExtraction(
            host,
            WhenEmbeddingImages(_ => Unavailable("The embedding provider failed (HTTP 502)")),
            Chunk("a", "caption", imageUrl: "https://i.imgur.com/a.png"));
        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        var indexed = await ReadSourceAsync(key);
        indexed!.ImageCount.Should().Be(0);
        indexed.ImagesDeferred.Should().BeTrue();

        // Owed pictures make it due again at once, which is when the archive tends to time out a second time.
        index.ClearReceivedCalls();
        ArrangeExtractor(host).ExtractAsync(Arg.Any<SourceDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Success(
                new ExtractedDocument("Tunisia", null, [Chunk("p0", "caption")], Degraded: true)));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Failed.Should().Be(1);
        await index.DidNotReceive().UpsertAsync(
            Arg.Any<IReadOnlyList<KnowledgePoint>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        var stored = await ReadSourceAsync(key);
        stored!.ImagesDeferred.Should().BeTrue("the pictures are still owed, and come back once the archive answers");
        stored.ContentHash.Should().Be(indexed.ContentHash);
    }

    [Fact]
    public async Task Ingest_IndexesADegradedTextExport_WhenNothingBetterIsIndexedYet()
    {
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        ArrangeExtractor(host).ExtractAsync(Arg.Any<SourceDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Success(
                new ExtractedDocument("Tunisia", null, [Chunk("p0", "Tunisian bollards are short.")], Degraded: true)));
        host.Mock<IEmbedder>().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => EmbedEverything((IReadOnlyList<EmbeddingInput>)call[0]));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Ingested.Should().Be(1, "the text is far better than nothing");
    }

    [Fact]
    public async Task Ingest_RemovesTheChunksOfASourceNoCatalogueHasListedForTheGracePeriod()
    {
        // A sync tombstones rather than deletes, but the chunks of a withdrawn guide stayed in the index
        // for ever and answers kept citing it.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var kept = NewKey();
        var withdrawn = NewKey();
        var justDropped = NewKey();
        ArrangeCatalog(host, Descriptor(kept), Descriptor(withdrawn), Descriptor(justDropped));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        var index = ArrangeExtraction(host, Chunk("a", "text"));
        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        ArrangeCatalog(host, Descriptor(kept));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        await BackdateRemovalAsync(withdrawn, TimeSpan.FromDays(31));
        index.ClearReceivedCalls();

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Removed.Should().Be(1);
        await index.Received(1).SweepAsync("plonkit", withdrawn, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await index.DidNotReceive().SweepAsync("plonkit", justDropped, Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await ReadSourceAsync(withdrawn))!.HoldsIndexedContent.Should().BeFalse();

        // Once is enough: the next run has nothing left to remove.
        index.ClearReceivedCalls();
        (await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10))).Value.Removed.Should().Be(0);
    }

    [Fact]
    public async Task Ingest_RetriesNoMoreFailingSourcesThanConfigured()
    {
        await ResetSourcesAsync();

        var now = DateTimeOffset.UtcNow;
        await SeedAsync([.. Enumerable.Range(1, 4).Select(_ => Failing(failures: 1, attemptedAt: now.AddDays(-4)))]);

        using var host = CreateHost(maxFailingRetries: 2);
        ArrangeExtraction(host, Chunk("a", "text"));

        var result = await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        result.Value.Attempted.Should().Be(2);
    }

    [Fact]
    public async Task ReadDue_FindsADueSourceBehindManyNotYetDueOnes()
    {
        // The queue read the limit×4 longest-waiting sources and only then asked which were due. A hundred
        // indexed ten days ago — not due for another four — hid a failing source whose backoff had passed,
        // and the run did less than was waiting, or nothing.
        await ResetSourcesAsync();

        var now = DateTimeOffset.UtcNow;
        var notYetDue = Enumerable.Range(0, 120).Select(_ => Indexed(now.AddDays(-10))).ToList();
        var due = Failing(failures: 1, attemptedAt: now.AddDays(-3));
        await SeedAsync([.. notYetDue, due]);

        var picked = await ReadDueAsync(now, limit: 25, maxRetries: 5);

        picked.Should().ContainSingle().Which.NaturalKey.Should().Be(due.NaturalKey);
    }

    [Fact]
    public async Task ReadDue_TakesNeverIndexedSourcesFirst_ThenOwedPictures_ThenReindexes_ThenFailingOnes()
    {
        await ResetSourcesAsync();

        var now = DateTimeOffset.UtcNow;
        var failing = Failing(failures: 1, attemptedAt: now.AddDays(-30));
        var reindex = Indexed(now.AddDays(-20));
        var owedPictures = Indexed(now.AddHours(-1), imagesDeferred: true);
        var neverIndexed = NewSource();
        await SeedAsync(failing, reindex, owedPictures, neverIndexed);

        var picked = await ReadDueAsync(now, limit: 10, maxRetries: 5);

        picked.Select(source => source.NaturalKey).Should().Equal(
            neverIndexed.NaturalKey, owedPictures.NaturalKey, reindex.NaturalKey, failing.NaturalKey);
    }

    [Fact]
    public async Task ReadDue_RetriesOnlyAFewFailingSourcesARun_FewestFailuresFirst()
    {
        // Ten dead links that shared a timestamp came due together and took ten of a run's twenty-five slots.
        await ResetSourcesAsync();

        var now = DateTimeOffset.UtcNow;
        await SeedAsync([.. Enumerable.Range(1, 8).Reverse().Select(failures => Failing(failures, now.AddDays(-4)))]);

        var picked = await ReadDueAsync(now, limit: 25, maxRetries: 5);

        picked.Select(source => source.ConsecutiveFailures).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task ReadDue_RestrictsToOneFamily_BeforeTheLimitIsApplied()
    {
        // Filtered afterwards, a run for one family was handed the first sources of every family and kept
        // whichever few happened to be its own.
        await ResetSourcesAsync();

        await SeedAsync([
            .. Enumerable.Range(0, 3).Select(_ => NewSource("imgur")),
            .. Enumerable.Range(0, 3).Select(_ => NewSource())
        ]);

        await using var db = fixture.CreateDbContext();
        var picked = await new Infrastructure.OutputAdapters.Repositories.EfKnowledgeSourceRepository(db)
            .ReadDueForIngestAsync(DateTimeOffset.UtcNow, Schedule, limit: 3, maxRetries: 5, sourceType: "plonkit");

        picked.Should().HaveCount(3).And.OnlyContain(source => source.SourceType == "plonkit");
    }

    [Fact]
    public async Task Retry_BringsBackSkippedSources_SoAFixedExtractorCanReadThem()
    {
        // rmrg.me changed its markup and every guide was skipped as "not a guide page". Skipping is for
        // good, so once the extractor was fixed nothing could bring them back.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var key = NewKey();
        ArrangeCatalog(host, Descriptor(key));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        ArrangeFailure(host, Error.Validation("ai.not_a_guide_page", "This page carries no guide content."));
        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));

        var retry = await host.SendAsync(new RetryKnowledgeSourcesCommand(SourceType: "plonkit"));

        retry.Value.Should().Be(new KnowledgeSourceRetryReport(Matched: 1, Queued: 1));

        ArrangeExtraction(host, Chunk("a", "Greek bollards have a red band."));
        (await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10))).Value.Ingested.Should().Be(1);
    }

    [Fact]
    public async Task Retry_ByKeyOrLink_QueuesOnlyThatSource()
    {
        await ResetSourcesAsync();

        var now = DateTimeOffset.UtcNow;
        var first = Failing(failures: 2, attemptedAt: now);
        var second = Failing(failures: 2, attemptedAt: now);
        var indexed = Indexed(now);
        await SeedAsync(first, second, indexed);

        using var host = CreateHost();

        (await host.SendAsync(new RetryKnowledgeSourcesCommand(first.NaturalKey))).Value
            .Should().Be(new KnowledgeSourceRetryReport(Matched: 1, Queued: 1));
        (await host.SendAsync(new RetryKnowledgeSourcesCommand(indexed.Url))).Value
            .Should().Be(new KnowledgeSourceRetryReport(Matched: 1, Queued: 0), "an indexed source is not a problem to retry");

        (await ReadSourceAsync(first.NaturalKey))!.Status.Should().Be(KnowledgeSourceStatus.Pending);
        (await ReadSourceAsync(second.NaturalKey))!.Status.Should().Be(KnowledgeSourceStatus.Failed);
    }

    [Fact]
    public async Task Retry_TakesTheTypedKeyTheListingShows()
    {
        // `/ai sources` prints each source as type:key, so that is what gets pasted back.
        await ResetSourcesAsync();

        var failing = Failing(failures: 1, attemptedAt: DateTimeOffset.UtcNow);
        await SeedAsync(failing);

        using var host = CreateHost();

        (await host.SendAsync(new RetryKnowledgeSourcesCommand($"imgur:{failing.NaturalKey}"))).IsFailure
            .Should().BeTrue("the type in the key must match the source's");
        (await host.SendAsync(new RetryKnowledgeSourcesCommand($"plonkit:{failing.NaturalKey}"))).Value
            .Should().Be(new KnowledgeSourceRetryReport(Matched: 1, Queued: 1));

        (await ReadSourceAsync(failing.NaturalKey))!.Status.Should().Be(KnowledgeSourceStatus.Pending);
    }

    [Fact]
    public async Task Retry_Refuses_WithoutBeingToldWhichSources()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new RetryKnowledgeSourcesCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task List_ShowsEachProblemSource_WithWhyAndWhenItIsNextTried()
    {
        await ResetSourcesAsync();

        var now = DateTimeOffset.UtcNow;
        var gone = NewSource();
        gone.MarkUnavailable("This document no longer exists (HTTP 410).", now.AddDays(-1));
        var failing = Failing(failures: 1, attemptedAt: now.AddHours(-1));
        var skipped = NewSource();
        skipped.MarkSkipped("This page carries no guide content.", now.AddDays(-2));
        await SeedAsync(gone, failing, skipped, Indexed(now));

        using var host = CreateHost();

        var result = await host.SendAsync(new ListKnowledgeSourcesQuery());

        result.Value.Total.Should().Be(3, "an indexed source is no problem");

        var listed = result.Value.Sources.ToDictionary(source => source.NaturalKey);
        listed[gone.NaturalKey].StatusReason.Should().Contain("410");
        listed[gone.NaturalKey].NextAttemptAtUtc.Should().BeCloseTo(now.AddDays(29), TimeSpan.FromSeconds(1));
        listed[failing.NaturalKey].NextAttemptAtUtc.Should().BeCloseTo(now.AddHours(1), TimeSpan.FromSeconds(1));
        listed[skipped.NaturalKey].NextAttemptAtUtc.Should().BeNull();
        listed[skipped.NaturalKey].IsDue.Should().BeFalse();

        var unavailable = await host.SendAsync(new ListKnowledgeSourcesQuery(KnowledgeSourceListKind.Unavailable));
        unavailable.Value.Sources.Should().ContainSingle().Which.NaturalKey.Should().Be(gone.NaturalKey);
    }

    [Fact]
    public async Task Status_CountsAnUnlistedSourceOnce_AsNoLongerListed()
    {
        // Counted as indexed too, a withdrawn guide went on inflating that figure after its chunks had gone.
        await ResetSourcesAsync();

        using var host = CreateHost();
        var kept = NewKey();
        var withdrawn = NewKey();
        ArrangeCatalog(host, Descriptor(kept), Descriptor(withdrawn));
        await host.SendAsync(new SyncSourceCatalogsCommand());
        ArrangeExtraction(host, Chunk("a", "text"));
        await host.SendAsync(new IngestKnowledgeSourcesCommand(MaxSources: 10));
        ArrangeCatalog(host, Descriptor(kept));
        await host.SendAsync(new SyncSourceCatalogsCommand());

        var counts = await host.SendAsync(new ReadKnowledgeSourceStatusQuery());

        counts.Value.Should().Be(new KnowledgeSourceCounts(Total: 2, Pending: 0, Ingested: 1, Failed: 0, Skipped: 0,
            Unavailable: 0, Removed: 1));
    }

    private static readonly KnowledgeSourceSchedule Schedule = new(TimeSpan.FromDays(14), TimeSpan.FromDays(30));

    private async Task<IReadOnlyList<KnowledgeSource>> ReadDueAsync(DateTimeOffset now, int limit, int maxRetries)
    {
        await using var db = fixture.CreateDbContext();
        return await new Infrastructure.OutputAdapters.Repositories.EfKnowledgeSourceRepository(db)
            .ReadDueForIngestAsync(now, Schedule, limit, maxRetries);
    }

    private async Task SeedAsync(params KnowledgeSource[] seeded)
    {
        await using var db = fixture.CreateDbContext();
        db.AddRange(seeded);
        await db.SaveChangesAsync();
    }

    private static KnowledgeSource NewSource(string sourceType = "plonkit")
    {
        var key = NewKey();
        return KnowledgeSource.Create(sourceType, key, $"https://www.plonkit.net/{key}", KnowledgeSourceOrigin.Sync,
            DateTimeOffset.UtcNow.AddDays(-100), country: key);
    }

    /// <summary>A never-indexed source fetched from <paramref name="host"/>, which is what a refusal is about.</summary>
    private static KnowledgeSource SourceOn(string host)
    {
        var key = NewKey();
        return KnowledgeSource.Create("plonkit", key, $"https://{host}/{key}", KnowledgeSourceOrigin.Sync,
            DateTimeOffset.UtcNow.AddDays(-100), country: key);
    }

    private static KnowledgeSource Indexed(DateTimeOffset at, bool imagesDeferred = false)
    {
        var source = NewSource();
        source.MarkIngested("hash", null, chunkCount: 1, imageCount: 0, at, imagesDeferred);
        return source;
    }

    private static KnowledgeSource Failing(int failures, DateTimeOffset attemptedAt)
    {
        var source = NewSource();
        for (var failure = 0; failure < failures; failure++)
        {
            source.MarkFailed("Could not fetch this guide page (HTTP 503).", attemptedAt);
        }

        return source;
    }

    private static void ArrangeFailure(MediatorTestHost host, Error error) =>
        ArrangeExtractor(host)
            .ExtractAsync(Arg.Any<SourceDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Failure(error));

    private async Task ResetSourcesAsync()
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "KnowledgeSources" """);
    }

    private Task BackdateLastAttemptAsync() => BackdateLastAttemptAsync(TimeSpan.FromDays(400));

    private async Task BackdateLastAttemptAsync(TimeSpan age)
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "KnowledgeSources" SET "LastAttemptedAtUtc" = {DateTimeOffset.UtcNow - age}""");
    }

    private async Task BackdateRemovalAsync(string naturalKey, TimeSpan age)
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "KnowledgeSources" SET "RemovedFromSyncAtUtc" = {DateTimeOffset.UtcNow - age} WHERE "NaturalKey" = {naturalKey}""");
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

    /// <summary>
    /// A host with several catalogues, which is the real shape: one per site that publishes an index,
    /// plus a library that links to many of the same resources.
    /// </summary>
    private MediatorTestHost CreateHostWithCatalogs(params (string Type, SourceDescriptor[] Sources)[] catalogs) =>
        new(fixture.ConnectionString,
            configure: services =>
            {
                foreach (var (type, descriptors) in catalogs)
                {
                    var catalog = Substitute.For<ISourceCatalog>();
                    catalog.SourceType.Returns(type);
                    catalog.ListAsync(Arg.Any<CancellationToken>())
                        .Returns(Result<IReadOnlyList<SourceDescriptor>>.Success(descriptors));

                    services.AddSingleton(catalog);
                }
            },
            configurationValues: new Dictionary<string, string?> { ["AI:Active"] = "true" });

    private MediatorTestHost CreateHost(
        int dailyBudget = 1000,
        int ingestionPercent = 100,
        int batchSize = 32,
        int maxRequestBytes = 8 * 1024 * 1024,
        bool embedImages = true,
        int maxFailingRetries = 5) =>
        new(fixture.ConnectionString, configurationValues: new Dictionary<string, string?>
        {
            ["AI:Ingestion:MaxFailingRetriesPerRun"] = maxFailingRetries.ToString(),
            ["AI:Ingestion:UnavailableRecheckDays"] = "30",
            ["AI:Ingestion:RemovedSourceGraceDays"] = "30",
            ["AI:Active"] = "true",
            ["AI:OpenRouter:DailyRequestBudget"] = dailyBudget.ToString(),
            ["AI:OpenRouter:EmbeddingBatchSize"] = batchSize.ToString(),
            ["AI:OpenRouter:EmbeddingMaxRequestBytes"] = maxRequestBytes.ToString(),
            ["AI:Ingestion:MaxSourcesPerRun"] = "25",
            // Unrestricted by default so the other tests reason about one number, not two.
            ["AI:Ingestion:MaxDailyBudgetPercent"] = ingestionPercent.ToString(),
            ["AI:Ingestion:EmbedImages"] = embedImages.ToString(),
            ["AI:MaxRequestsPerUserPerHour"] = "0"
        });

    /// <summary>Scripts the chat ports so a question can be asked alongside an indexing run.</summary>
    private static void ArrangeChat(MediatorTestHost host)
    {
        host.Mock<IChatModelCatalog>().ReadChainAsync(Arg.Any<ChatModelRequirements>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["test/model"]));

        host.Mock<IChatModelClient>().CompleteAsync(Arg.Any<AiChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<AiChatResponse>.Success(
                new AiChatResponse("an answer", "test/model", new AiTokenUsage(1, 1))));
    }

    private static ulong NewSnowflake() => (ulong)Random.Shared.NextInt64(1_000_000_000, long.MaxValue);

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
        ArrangeExtraction(host, EmbedEverything, chunks);

    /// <summary>
    /// Scripts a successful extraction plus the embedder and index it feeds. How each embedding request is
    /// answered is decided inside a single callback rather than by overlapping argument matchers, so which
    /// stub answers a given call is unambiguous.
    /// </summary>
    private static IKnowledgeIndex ArrangeExtraction(
        MediatorTestHost host,
        Func<IReadOnlyList<EmbeddingInput>, Result<IReadOnlyList<ReadOnlyMemory<float>>>> embed,
        params ExtractedChunk[] chunks)
    {
        ArrangeExtractor(host)
            .ExtractAsync(Arg.Any<SourceDescriptor>(), Arg.Any<CancellationToken>())
            .Returns(Result<ExtractedDocument>.Success(new ExtractedDocument("Tunisia", null, chunks)));

        host.Mock<IEmbedder>().EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(call => embed((IReadOnlyList<EmbeddingInput>)call[0]));

        return host.Mock<IKnowledgeIndex>();
    }

    /// <summary>Embeds text normally and answers image requests with <paramref name="images"/>.</summary>
    private static Func<IReadOnlyList<EmbeddingInput>, Result<IReadOnlyList<ReadOnlyMemory<float>>>> WhenEmbeddingImages(
        Func<IReadOnlyList<ImageEmbeddingInput>, Result<IReadOnlyList<ReadOnlyMemory<float>>>> images) =>
        inputs => inputs.OfType<ImageEmbeddingInput>().ToList() is { Count: > 0 } imageInputs
            ? images(imageInputs)
            : EmbedEverything(inputs);

    private static Result<IReadOnlyList<ReadOnlyMemory<float>>> EmbedEverything(IReadOnlyList<EmbeddingInput> inputs) =>
        Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success([.. inputs.Select(_ => new ReadOnlyMemory<float>(new float[4]))]);

    private static Result<IReadOnlyList<ReadOnlyMemory<float>>> Rejected(string message) =>
        Result<IReadOnlyList<ReadOnlyMemory<float>>>.Failure(Error.Validation(EmbeddingErrorCodes.Rejected, message));

    private static Result<IReadOnlyList<ReadOnlyMemory<float>>> Unavailable(string message) =>
        Result<IReadOnlyList<ReadOnlyMemory<float>>>.Failure(Error.Unexpected(EmbeddingErrorCodes.Failed, message));

    private static Result<IReadOnlyList<ReadOnlyMemory<float>>> RateLimited() =>
        Result<IReadOnlyList<ReadOnlyMemory<float>>>.Failure(
            Error.Conflict(EmbeddingErrorCodes.RateLimited, "The embedding provider is rate-limiting us right now."));

    /// <summary>The image inputs of every embedding request that carried images, in the order they were sent.</summary>
    private static IReadOnlyList<IReadOnlyList<ImageEmbeddingInput>> ImageCalls(MediatorTestHost host) =>
    [
        .. host.Mock<IEmbedder>().ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEmbedder.EmbedAsync))
            .Select(call => ((IReadOnlyList<EmbeddingInput>)call.GetArguments()[0]!).OfType<ImageEmbeddingInput>().ToList())
            .Where(images => images.Count > 0)
    ];

    private static IReadOnlyList<KnowledgePoint> LastWrite(IKnowledgeIndex index) =>
        index.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IKnowledgeIndex.UpsertAsync))
            .Select(call => (IReadOnlyList<KnowledgePoint>)call.GetArguments()[0]!)
            .Last();

    /// <summary>One image chunk per key, each with an image named after its key.</summary>
    private static ExtractedChunk[] ImageChunks(params string[] keys) =>
        [.. keys.Select(key => Chunk(key, $"caption {key}", imageUrl: $"https://i.imgur.com/{key}.png"))];

    private static ExtractedChunk Chunk(string key, string text, string? imageUrl = null) =>
        new(key, "Tunisia > Identifying", text, imageUrl);

    private static SourceDescriptor Descriptor(string naturalKey, string? title = null) =>
        new("plonkit", naturalKey, new Uri($"https://www.plonkit.net/{naturalKey}"), title, Country: naturalKey);

    /// <summary>Unique per test so the shared container needs no cleanup between them.</summary>
    private static string NewKey() => $"country-{Guid.NewGuid():N}";
}

using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Ingestion;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>The admin side of the guide queue: retrying, listing and refreshing sources.</summary>
public sealed class KnowledgeSourceHandlersTests
{
    private const string Link = "https://example.com/guide";

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly IKnowledgeSourceRepository _sources = Substitute.For<IKnowledgeSourceRepository>();

    private static KnowledgeSource Source(
        string type = "gdoc",
        string key = "doc-1",
        KnowledgeSourceOrigin origin = KnowledgeSourceOrigin.Sync) =>
        KnowledgeSource.Create(type, key, Link, origin, Now);

    private static KnowledgeSource Failed(string key = "doc-1")
    {
        var source = Source(key: key);
        source.MarkFailed("boom", Now);
        return source;
    }

    // ---- Retry ------------------------------------------------------------

    private void Matching(string? key, string? type, params KnowledgeSource[] matched) =>
        _sources.ReadMatchingAsync(key, type, Arg.Any<CancellationToken>()).Returns(matched);

    private void NothingMatchesByDefault() =>
        _sources.ReadMatchingAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);

    private Task<Result<KnowledgeSourceRetryReport>> RetryAsync(string? keyOrUrl, string? sourceType = null) =>
        new RetryKnowledgeSourcesHandler(_sources, NullLogger<RetryKnowledgeSourcesHandler>.Instance)
            .Handle(new RetryKnowledgeSourcesCommand(keyOrUrl, sourceType), CancellationToken.None);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", " ")]
    public async Task Retry_NeedsAScope(string? keyOrUrl, string? sourceType)
    {
        var result = await RetryAsync(keyOrUrl, sourceType);

        result.Error.Code.Should().Be("ai.source_retry_unscoped");
        result.Error.Type.Should().Be(ErrorType.Validation);
        _sources.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Retry_ByType_IsScopeEnough()
    {
        var failed = Failed();
        Matching(null, "gdoc", failed);

        var result = await RetryAsync(null, "gdoc");

        result.Value.Should().Be(new KnowledgeSourceRetryReport(1, 1));
        failed.Status.Should().Be(KnowledgeSourceStatus.Pending);
    }

    [Fact]
    public async Task Retry_CountsWhatMatched_AndWhatWasQueued_Separately()
    {
        Matching("doc", null, Failed("a"), Source(key: "already-waiting"));

        var result = await RetryAsync("doc");

        result.Value.Should().Be(new KnowledgeSourceRetryReport(2, 1));
    }

    [Fact]
    public async Task Retry_NothingMatching_IsNotFound()
    {
        NothingMatchesByDefault();

        var result = await RetryAsync("nope");

        result.Error.Code.Should().Be("ai.source_not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Retry_ReadsTheFormTheListingShows()
    {
        NothingMatchesByDefault();
        var failed = Failed();
        Matching("doc-1", "gdoc", failed);

        var result = await RetryAsync("gdoc:doc-1");

        result.Value.Matched.Should().Be(1);
    }

    [Fact]
    public async Task Retry_TriesAKeyAsItIsFirst()
    {
        NothingMatchesByDefault();
        Matching("gdoc:doc-1", null, Failed());

        await RetryAsync("gdoc:doc-1");

        await _sources.DidNotReceive().ReadMatchingAsync("doc-1", "gdoc", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retry_AcceptsATypedKey_WhenTheTypeNamedAgrees()
    {
        NothingMatchesByDefault();
        Matching("doc-1", "gdoc", Failed());

        var result = await RetryAsync(" gdoc:doc-1 ", " GDOC ");

        result.Value.Matched.Should().Be(1);
    }

    [Fact]
    public async Task Retry_RefusesATypedKey_WhenTheTypeNamedDisagrees()
    {
        NothingMatchesByDefault();
        Matching("doc-1", "gdoc", Failed());

        var result = await RetryAsync("gdoc:doc-1", "album");

        result.Error.Code.Should().Be("ai.source_not_found");
    }

    [Theory]
    [InlineData("https://example.com/guide")]
    [InlineData("http://example.com/guide")]
    public async Task Retry_NeverReadsALinkAsATypedKey(string link)
    {
        NothingMatchesByDefault();

        await RetryAsync(link);

        await _sources.DidNotReceive().ReadMatchingAsync(Arg.Any<string?>(), Arg.Is<string?>(t => t != null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("no-separator")]
    [InlineData(":leading")]
    [InlineData("trailing:")]
    public async Task Retry_NeedsBothSidesOfATypedKey(string text)
    {
        NothingMatchesByDefault();

        var result = await RetryAsync(text);

        result.IsFailure.Should().BeTrue();
        await _sources.Received(1).ReadMatchingAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ---- List -------------------------------------------------------------

    private KnowledgeSourceListFilter? _filter;
    private int? _limit;

    private Task<Result<KnowledgeSourceListing>> ListAsync(
        KnowledgeSourceListKind kind = KnowledgeSourceListKind.Problems,
        string? sourceType = null,
        int limit = 15,
        int total = 1,
        params KnowledgeSource[] page)
    {
        _sources.ListAsync(Arg.Any<KnowledgeSourceListFilter>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _filter = call.Arg<KnowledgeSourceListFilter>();
                _limit = call.Arg<int>();
                return new KnowledgeSourcePage(page, total);
            });

        var configuration = new AiIngestionConfiguration { ReingestAfterDays = 30, UnavailableRecheckDays = 60 };
        return new ListKnowledgeSourcesHandler(_sources, Options.Create(configuration))
            .Handle(new ListKnowledgeSourcesQuery(kind, sourceType, limit), CancellationToken.None);
    }

    [Theory]
    [InlineData(KnowledgeSourceListKind.Failed, KnowledgeSourceStatus.Failed)]
    [InlineData(KnowledgeSourceListKind.Skipped, KnowledgeSourceStatus.Skipped)]
    [InlineData(KnowledgeSourceListKind.Unavailable, KnowledgeSourceStatus.Unavailable)]
    [InlineData(KnowledgeSourceListKind.Pending, KnowledgeSourceStatus.Pending)]
    public async Task List_OfOneKind_FiltersByThatStatus(KnowledgeSourceListKind kind, KnowledgeSourceStatus status)
    {
        await ListAsync(kind);

        _filter!.Statuses.Should().Equal(status);
        _filter.RemovedFromSync.Should().BeFalse();
    }

    [Fact]
    public async Task List_OfProblems_IsEverythingAnAdminCouldAct_On()
    {
        await ListAsync();

        _filter!.Statuses.Should().BeEquivalentTo(
        [
            KnowledgeSourceStatus.Failed,
            KnowledgeSourceStatus.Skipped,
            KnowledgeSourceStatus.Unavailable
        ]);
    }

    [Fact]
    public async Task List_OfRemoved_IgnoresTheStatus()
    {
        await ListAsync(KnowledgeSourceListKind.Removed);

        _filter!.Statuses.Should().BeEmpty();
        _filter.RemovedFromSync.Should().BeTrue();
    }

    [Theory]
    [InlineData(KnowledgeSourceListKind.Problems)]
    [InlineData(KnowledgeSourceListKind.Removed)]
    [InlineData(KnowledgeSourceListKind.Failed)]
    [InlineData(KnowledgeSourceListKind.Skipped)]
    [InlineData(KnowledgeSourceListKind.Unavailable)]
    [InlineData(KnowledgeSourceListKind.Pending)]
    public async Task List_RestrictsEveryKindToTheFamily_Trimmed(KnowledgeSourceListKind kind)
    {
        await ListAsync(kind, " gdoc ");

        _filter!.SourceType.Should().Be("gdoc");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task List_WithoutAFamily_ListsEveryFamily(string? sourceType)
    {
        await ListAsync(sourceType: sourceType);

        _filter!.SourceType.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 10)]
    [InlineData(25, 25)]
    [InlineData(500, 25)]
    public async Task List_ClampsTheLimit(int requested, int expected)
    {
        await ListAsync(limit: requested);

        _limit.Should().Be(expected);
    }

    [Fact]
    public async Task List_ReportsTheTotal_AndEachSourcesFields()
    {
        var source = Source();
        source.MarkFailed("boom", Now.AddHours(-1));

        var listing = (await ListAsync(total: 7, page: [source])).Value;

        listing.Total.Should().Be(7);
        var summary = listing.Sources.Should().ContainSingle().Subject;
        summary.SourceType.Should().Be("gdoc");
        summary.NaturalKey.Should().Be("doc-1");
        summary.Url.Should().Be(Link);
        summary.Status.Should().Be(KnowledgeSourceStatus.Failed);
        summary.StatusReason.Should().Be("boom");
        summary.ConsecutiveFailures.Should().Be(1);
        summary.LastAttemptedAtUtc.Should().Be(Now.AddHours(-1));
    }

    [Fact]
    public async Task List_ShowsWhenASourceIsNextTried()
    {
        var source = Source();
        source.MarkFailed("boom", Now);

        var summary = (await ListAsync(page: [source])).Value.Sources.Single();

        summary.NextAttemptAtUtc.Should().Be(Now.AddHours(2));
        summary.IsDue.Should().BeFalse();
    }

    [Fact]
    public async Task List_ShowsNoTimeForASourceAlreadyDue()
    {
        var summary = (await ListAsync(page: [Source()])).Value.Sources.Single();

        summary.NextAttemptAtUtc.Should().BeNull();
        summary.IsDue.Should().BeTrue();
    }

    [Fact]
    public async Task List_ShowsNeitherForASourceThatNeverComesDue()
    {
        var source = Source();
        source.MarkSkipped("no guide", Now);

        var summary = (await ListAsync(page: [source])).Value.Sources.Single();

        summary.NextAttemptAtUtc.Should().BeNull();
        summary.IsDue.Should().BeFalse();
    }

    [Fact]
    public async Task List_UsesTheConfiguredRecheck_ForAnUnavailableSource()
    {
        var source = Source();
        source.MarkUnavailable("deleted", Now);

        var summary = (await ListAsync(page: [source])).Value.Sources.Single();

        summary.NextAttemptAtUtc.Should().Be(Now.AddDays(60));
    }

    // ---- Catalogue sync ---------------------------------------------------

    private static SourceDescriptor Listed(string type = "gdoc", string key = "doc-1", string title = "Guide", string? unsupported = null) =>
        new(type, key, new Uri(Link), title, "Germany", "Europe", "Author", 3, unsupported);

    private static ISourceCatalog Catalog(string type, params SourceDescriptor[] listed)
    {
        var catalog = Substitute.For<ISourceCatalog>();
        catalog.SourceType.Returns(type);
        catalog.ListAsync(Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<SourceDescriptor>>.Success(listed));
        return catalog;
    }

    private static ISourceCatalog BrokenCatalog(string type)
    {
        var catalog = Substitute.For<ISourceCatalog>();
        catalog.SourceType.Returns(type);
        catalog.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SourceDescriptor>>.Failure(Error.Unexpected("x", "site is down")));
        return catalog;
    }

    private readonly List<KnowledgeSource> _added = [];

    private async Task<CatalogSyncReport> SyncAsync(ISourceCatalog[] catalogs, KnowledgeSource[] stored, string? sourceType = null)
    {
        _sources.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(stored);
        _sources.When(s => s.Add(Arg.Any<KnowledgeSource>())).Do(call => _added.Add(call.Arg<KnowledgeSource>()));

        var result = await new SyncSourceCatalogsHandler(catalogs, _sources, NullLogger<SyncSourceCatalogsHandler>.Instance)
            .Handle(new SyncSourceCatalogsCommand(sourceType), CancellationToken.None);
        return result.Value;
    }

    [Fact]
    public async Task Sync_AddsANewSource_WithItsCatalogueDetails()
    {
        var report = await SyncAsync([Catalog("gdoc", Listed())], []);

        report.Should().Be(new CatalogSyncReport(Discovered: 1, Added: 1, Updated: 0, Tombstoned: 0));
        var added = _added.Should().ContainSingle().Subject;
        added.SourceType.Should().Be("gdoc");
        added.NaturalKey.Should().Be("doc-1");
        added.Url.Should().Be(new Uri(Link).ToString());
        added.Origin.Should().Be(KnowledgeSourceOrigin.Sync);
        added.Title.Should().Be("Guide");
        added.Country.Should().Be("Germany");
        added.Continent.Should().Be("Europe");
        added.Author.Should().Be("Author");
        added.Priority.Should().Be(3);
        added.Status.Should().Be(KnowledgeSourceStatus.Pending);
    }

    [Fact]
    public async Task Sync_RecordsAnUnsupportedSource_WithoutQueueingIt()
    {
        await SyncAsync([Catalog("gdoc", Listed(unsupported: "a video"))], []);

        var added = _added.Should().ContainSingle().Subject;
        added.Status.Should().Be(KnowledgeSourceStatus.Skipped);
        added.StatusReason.Should().Be("a video");
    }

    [Fact]
    public async Task Sync_RefreshesAKnownSource_LeavingItsIngestionStateAlone()
    {
        var known = Failed();

        var report = await SyncAsync([Catalog("gdoc", Listed(title: "Renamed"))], [known]);

        report.Should().Be(new CatalogSyncReport(1, 0, 1, 0));
        known.Title.Should().Be("Renamed");
        known.Status.Should().Be(KnowledgeSourceStatus.Failed);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_MatchesKnownSourcesWhateverTheirCase()
    {
        var report = await SyncAsync([Catalog("gdoc", Listed("GDOC", "DOC-1"))], [Source()]);

        report.Updated.Should().Be(1);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_TheFirstListingWins_WhenAResourceIsListedTwice()
    {
        var report = await SyncAsync(
            [Catalog("site", Listed(title: "From the site")), Catalog("library", Listed(title: "From the library"))],
            []);

        report.Should().Be(new CatalogSyncReport(1, 1, 0, 0));
        _added.Should().ContainSingle().Which.Title.Should().Be("From the site");
    }

    [Fact]
    public async Task Sync_TellsSourcesOfDifferentFamiliesApart()
    {
        var report = await SyncAsync([Catalog("mixed", Listed("gdoc"), Listed("album"))], []);

        report.Added.Should().Be(2);
    }

    [Fact]
    public async Task Sync_OfOneFamily_OnlyReadsThatCatalogue()
    {
        var other = Catalog("album");

        var report = await SyncAsync([Catalog("gdoc", Listed()), other], [], "GDOC");

        report.Added.Should().Be(1);
        await other.DidNotReceiveWithAnyArgs().ListAsync(default);
    }

    [Fact]
    public async Task Sync_TombstonesWhatIsNoLongerListed()
    {
        var gone = Source(key: "gone");

        var report = await SyncAsync([Catalog("gdoc", Listed())], [gone]);

        report.Tombstoned.Should().Be(1);
        gone.RemovedFromSyncAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Sync_NeverTombstonesWhatAnAdminAdded()
    {
        var manual = Source(key: "manual", origin: KnowledgeSourceOrigin.Manual);

        var report = await SyncAsync([Catalog("gdoc", Listed())], [manual]);

        report.Tombstoned.Should().Be(0);
        manual.RemovedFromSyncAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Sync_KeepsTheFirstTombstoneDate()
    {
        var gone = Source(key: "gone");
        var removedAt = DateTimeOffset.UtcNow.AddDays(-3);
        gone.MarkRemovedFromSync(removedAt);

        var report = await SyncAsync([Catalog("gdoc", Listed())], [gone]);

        report.Tombstoned.Should().Be(0);
        gone.RemovedFromSyncAtUtc.Should().Be(removedAt);
    }

    [Fact]
    public async Task Sync_ACatalogueThatFailed_DoesNotLookLikeItDroppedEverything()
    {
        var other = Source("album", "other");

        var report = await SyncAsync([BrokenCatalog("album"), Catalog("gdoc", Listed())], [other]);

        report.Tombstoned.Should().Be(0);
        other.RemovedFromSyncAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Sync_AfterAFailure_StillSweepsTheFamiliesThatWereRead()
    {
        var gone = Source("gdoc", "gone");

        var report = await SyncAsync([BrokenCatalog("album"), Catalog("gdoc", Listed())], [gone]);

        report.Tombstoned.Should().Be(1);
    }

    [Fact]
    public async Task Sync_OfOneFamily_NeverSweepsTheOthers()
    {
        var other = Source("album", "other");

        var report = await SyncAsync([Catalog("gdoc", Listed())], [other], "gdoc");

        report.Tombstoned.Should().Be(0);
    }

    [Fact]
    public async Task Sync_WithEveryCatalogueRead_SweepsAFamilyThatListedNothing()
    {
        var other = Source("album", "other");

        var report = await SyncAsync([Catalog("gdoc", Listed())], [other]);

        report.Tombstoned.Should().Be(1);
    }

    [Fact]
    public async Task Sync_BringsAReturnedSourceBack()
    {
        var returned = Source();
        returned.MarkRemovedFromSync(DateTimeOffset.UtcNow.AddDays(-3));

        await SyncAsync([Catalog("gdoc", Listed())], [returned]);

        returned.RemovedFromSyncAtUtc.Should().BeNull();
    }
}

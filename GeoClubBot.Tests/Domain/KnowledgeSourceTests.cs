using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

/// <summary>
/// The source's own schedule: when each state comes due again, and which states an admin can undo.
/// The queue and the <c>/ai sources</c> listing both read it from here, so it is pinned here.
/// </summary>
public sealed class KnowledgeSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 5, 0, 0, TimeSpan.Zero);

    private static readonly KnowledgeSourceSchedule Schedule = new(TimeSpan.FromDays(14), TimeSpan.FromDays(30));

    [Fact]
    public void AGoneSource_IsCheckedAgainOnlyAfterTheRecheckInterval()
    {
        // Retried with the failure backoff, nine deleted documents took a slot of every third run for ever.
        var source = NewSource();
        source.MarkUnavailable("This document no longer exists (HTTP 410).", Now);

        source.Status.Should().Be(KnowledgeSourceStatus.Unavailable);
        source.IsDueForIngest(Now.AddDays(29), Schedule).Should().BeFalse();
        source.IsDueForIngest(Now.AddDays(30), Schedule).Should().BeTrue();
        source.NextAttemptAtUtc(Schedule).Should().Be(Now.AddDays(30));
    }

    [Fact]
    public void AnUnavailableSource_IsConfirmedOnlyOnTheSecondCheckInARow()
    {
        // One check is not enough to drop what is indexed: a guide briefly unshared would lose it all.
        var source = NewSource();
        source.MarkFailed("timed out", Now);
        source.MarkFailed("timed out", Now.AddDays(1));

        source.MarkUnavailable("gone", Now.AddDays(2));
        source.ConsecutiveFailures.Should().Be(1, "earlier failures of another kind are not checks that found it gone");
        source.IsConfirmedUnavailable.Should().BeFalse();

        source.MarkUnavailable("gone", Now.AddDays(32));
        source.IsConfirmedUnavailable.Should().BeTrue();
    }

    [Fact]
    public void AnUnavailableSource_RecoversByItself_WhenItCanBeReadAgain()
    {
        var source = NewSource();
        source.MarkUnavailable("This document is not publicly readable (HTTP 403).", Now);

        source.MarkIngested("hash", null, chunkCount: 3, imageCount: 0, Now.AddDays(30));

        source.Status.Should().Be(KnowledgeSourceStatus.Ingested);
        source.StatusReason.Should().BeNull();
        source.ConsecutiveFailures.Should().Be(0);
    }

    [Theory]
    [InlineData(KnowledgeSourceStatus.Failed)]
    [InlineData(KnowledgeSourceStatus.Skipped)]
    [InlineData(KnowledgeSourceStatus.Unavailable)]
    public void RequestRetry_MakesAProblemSourceDueAtOnce(KnowledgeSourceStatus status)
    {
        // A skipped guide used to stay skipped for good: rmrg.me changed its markup, every guide was
        // skipped as "not a guide page", and nothing could bring them back once the extractor was fixed.
        var source = InState(status);
        source.IsDueForIngest(Now, Schedule).Should().BeFalse();

        source.RequestRetry().Should().BeTrue();

        source.Status.Should().Be(KnowledgeSourceStatus.Pending);
        source.ConsecutiveFailures.Should().Be(0);
        source.IsRetrying.Should().BeFalse("a queued source must not count against the failing-retry cap");
        source.IsDueForIngest(Now, Schedule).Should().BeTrue();
        source.StatusReason.Should().Contain(status.ToString(), "the previous verdict stays readable until replaced");
    }

    [Fact]
    public void RequestRetry_LeavesAnIndexedSourceAlone()
    {
        var source = NewSource();
        source.MarkIngested("hash", null, 1, 0, Now);

        source.RequestRetry().Should().BeFalse();
        source.Status.Should().Be(KnowledgeSourceStatus.Ingested);
    }

    [Fact]
    public void ASkippedSource_NeverComesDueByItself()
    {
        var source = NewSource();
        source.MarkSkipped("not a guide page", Now);

        source.NextAttemptAtUtc(Schedule).Should().BeNull();
        source.IsDueForIngest(Now.AddYears(5), Schedule).Should().BeFalse();
    }

    [Fact]
    public void AnUnlistedSource_NeverComesDueByItself_WhateverItsState()
    {
        var source = NewSource();
        source.MarkFailed("boom", Now);
        source.MarkRemovedFromSync(Now);

        source.NextAttemptAtUtc(Schedule).Should().BeNull();
    }

    [Fact]
    public void MarkIndexedContentRemoved_ForgetsTheHash_SoAReturningSourceIsWrittenInFull()
    {
        // Judged unchanged against chunks that no longer exist, a restored guide would never be indexed again.
        var source = NewSource();
        source.MarkIngested("hash", null, chunkCount: 4, imageCount: 2, Now);
        source.HoldsIndexedContent.Should().BeTrue();

        source.MarkIndexedContentRemoved();

        source.ContentHash.Should().BeNull();
        source.ChunkCount.Should().Be(0);
        source.ImageCount.Should().Be(0);
        source.HoldsIndexedContent.Should().BeFalse();
    }

    [Theory]
    [InlineData(2, false, true)]
    [InlineData(0, true, true)]
    [InlineData(0, false, false)]
    public void HoldsIndexedPictures_CountsPicturesStillOwedTheirEmbedding(int imageCount, bool imagesDeferred, bool holds)
    {
        // A source whose every picture was postponed has written image chunks and an image count of zero.
        // Judged by the count alone, a degraded text export replaced those chunks and cleared the debt.
        var source = NewSource();
        source.MarkIngested("hash", null, chunkCount: 4, imageCount, Now, imagesDeferred);

        source.HoldsIndexedPictures.Should().Be(holds);
    }

    [Fact]
    public void TheTimeSpanOverload_KeepsItsMeaning_ForAnythingButAnUnavailableSource()
    {
        var source = NewSource();
        source.MarkIngested("hash", null, 1, 0, Now);

        source.IsDueForIngest(Now.AddDays(13), TimeSpan.FromDays(14)).Should().BeFalse();
        source.IsDueForIngest(Now.AddDays(14), TimeSpan.FromDays(14)).Should().BeTrue();
    }

    private static KnowledgeSource InState(KnowledgeSourceStatus status)
    {
        var source = NewSource();

        switch (status)
        {
            case KnowledgeSourceStatus.Failed:
                source.MarkFailed("HTTP 500", Now);
                break;
            case KnowledgeSourceStatus.Skipped:
                source.MarkSkipped("This page carries no guide content.", Now);
                break;
            case KnowledgeSourceStatus.Unavailable:
                source.MarkUnavailable("HTTP 410", Now);
                break;
        }

        return source;
    }

    private static KnowledgeSource NewSource() =>
        KnowledgeSource.Create("gdoc", "abc123", "https://docs.google.com/document/d/abc123/edit",
            KnowledgeSourceOrigin.Sync, Now.AddDays(-100), title: "Tunisia");
}

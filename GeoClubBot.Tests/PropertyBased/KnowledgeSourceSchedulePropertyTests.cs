using CsCheck;
using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// Invariants of a guide source's schedule over arbitrary histories of attempts: the time shown to an
/// admin as the next attempt is exactly when the queue takes the source, failures back off without ever
/// passing the cap, a deleted or private source waits out its whole recheck interval, and nothing but an
/// admin brings back a skipped one. Timestamps are UTC and kept far from the calendar's ends, so adding
/// an interval cannot overflow.
/// </summary>
public sealed class KnowledgeSourceSchedulePropertyTests
{
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The backoff after six failures in a row, which no later failure exceeds.</summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(64);

    private enum Step
    {
        Ingest,
        IngestWithImagesOwed,
        Unchanged,
        Fail,
        Skip,
        Unavailable,
        Retry
    }

    private static readonly Gen<KnowledgeSourceSchedule> GenSchedule =
        Gen.Select(Gen.Int[1, 60], Gen.Int[1, 90],
            (reingestDays, recheckDays) => new KnowledgeSourceSchedule(
                TimeSpan.FromDays(reingestDays), TimeSpan.FromDays(recheckDays)));

    /// <summary>A history of attempts, each a step and the hours since the one before.</summary>
    private static readonly Gen<List<(Step Step, int Hours)>> GenHistory =
        Gen.Select(Gen.Enum<Step>(), Gen.Int[0, 24 * 40], (step, hours) => (step, hours)).List[0, 12];

    [Fact]
    public void The_next_attempt_shown_is_exactly_when_the_source_comes_due() =>
        Gen.Select(GenHistory, GenSchedule, Gen.Int[0, 24 * 120]).Sample((history, schedule, laterHours) =>
        {
            var (source, last) = Replay(history);
            var now = last.AddHours(laterHours);

            var next = source.NextAttemptAtUtc(schedule);

            source.IsDueForIngest(now, schedule).Should().Be(next is { } at && at <= now);
        });

    [Fact]
    public void Failures_back_off_doubling_and_never_past_the_cap() =>
        Gen.Select(Gen.Int[1, 20], GenSchedule).Sample((failures, schedule) =>
        {
            var source = NewSource();
            var at = Origin;

            for (var failure = 0; failure < failures; failure++)
            {
                at = at.AddHours(1);
                source.MarkFailed("HTTP 500", at);
            }

            var backoff = source.NextAttemptAtUtc(schedule)!.Value - at;

            backoff.Should().Be(TimeSpan.FromHours(Math.Pow(2, Math.Min(failures, 6))));
            backoff.Should().BeLessThanOrEqualTo(MaxBackoff);
            source.IsRetrying.Should().BeTrue();
        });

    [Fact]
    public void An_unavailable_source_waits_out_its_whole_recheck_interval() =>
        Gen.Select(GenHistory, GenSchedule, Gen.Int[0, 24 * 120]).Sample((history, schedule, laterHours) =>
        {
            var (source, last) = Replay(history);
            var checkedAt = last.AddHours(1);
            source.MarkUnavailable("This document no longer exists (HTTP 410).", checkedAt);

            var now = checkedAt.AddHours(laterHours);

            source.IsDueForIngest(now, schedule)
                .Should().Be(now - checkedAt >= schedule.UnavailableRecheckInterval);
        });

    [Fact]
    public void A_skipped_source_comes_back_only_when_an_admin_asks() =>
        Gen.Select(GenHistory, GenSchedule, Gen.Int[0, 24 * 365]).Sample((history, schedule, laterHours) =>
        {
            var (source, last) = Replay(history);
            source.MarkSkipped("This page carries no guide content.", last.AddHours(1));

            source.IsDueForIngest(last.AddHours(1 + laterHours), schedule).Should().BeFalse();

            source.RequestRetry().Should().BeTrue();
            source.IsDueForIngest(last.AddHours(1), schedule).Should().BeTrue();
        });

    [Fact]
    public void Only_an_unreadable_source_is_ever_confirmed_gone() =>
        GenHistory.Sample(history =>
        {
            var (source, _) = Replay(history);

            if (source.IsConfirmedUnavailable)
            {
                source.Status.Should().Be(KnowledgeSourceStatus.Unavailable);
                source.ConsecutiveFailures.Should().BeGreaterThanOrEqualTo(2);
            }
        });

    private static (KnowledgeSource Source, DateTimeOffset Last) Replay(List<(Step Step, int Hours)> history)
    {
        var source = NewSource();
        var at = Origin;

        foreach (var (step, hours) in history)
        {
            at = at.AddHours(hours);

            switch (step)
            {
                case Step.Ingest:
                    source.MarkIngested("hash", null, 3, 1, at);
                    break;
                case Step.IngestWithImagesOwed:
                    source.MarkIngested("hash", null, 3, 0, at, imagesDeferred: true);
                    break;
                case Step.Unchanged:
                    source.MarkUnchanged(at);
                    break;
                case Step.Fail:
                    source.MarkFailed("HTTP 500", at);
                    break;
                case Step.Skip:
                    source.MarkSkipped("not a guide page", at);
                    break;
                case Step.Unavailable:
                    source.MarkUnavailable("HTTP 410", at);
                    break;
                case Step.Retry:
                    source.RequestRetry();
                    break;
            }
        }

        return (source, at);
    }

    private static KnowledgeSource NewSource() =>
        KnowledgeSource.Create("gdoc", "abc123", "https://docs.google.com/document/d/abc123/edit",
            KnowledgeSourceOrigin.Sync, Origin);
}

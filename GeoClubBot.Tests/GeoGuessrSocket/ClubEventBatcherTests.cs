using System.Collections.Concurrent;
using Configuration;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Infrastructure.InputAdapters.GeoGuessrSocket;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.UseCases.ClubEvents;
using Xunit;

namespace GeoClubBot.Tests.GeoGuessrSocket;

public sealed class ClubEventBatcherTests : IAsyncLifetime
{
    private static readonly Guid ClubA = Guid.NewGuid();
    private static readonly Guid ClubB = Guid.NewGuid();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ConcurrentQueue<IReadOnlyList<ClubPushEvent>> _batches = new();
    private readonly CancellationTokenSource _stop = new();
    private ClubEventBatcher _batcher = null!;
    private Task? _run;

    public Task InitializeAsync()
    {
        _sender.Send(Arg.Any<ApplyClubEventsCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _batches.Enqueue(call.Arg<ApplyClubEventsCommand>().Events);
            return true;
        });
        _batcher = new ClubEventBatcher(
            TestScopes.Serving(services => services.AddSingleton(_sender)),
            Options.Create(new GeoGuessrSocketConfiguration { BatchWindow = TimeSpan.FromSeconds(10) }),
            _clock, NullLogger<ClubEventBatcher>.Instance);
        _run = _batcher.RunAsync(_stop.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        await _run!.Invoking(t => t.WaitAsync(TimeSpan.FromSeconds(5))).Should().ThrowAsync<OperationCanceledException>();
        _stop.Dispose();
    }

    private static ClubMissionBoardUpdatedPush Board(Guid club) => new(club, default);

    [Fact]
    public async Task EventsWithinTheWindow_AreAppliedTogether_NumberedInArrivalOrder()
    {
        _batcher.Publish(Board(ClubA));
        await _clock.AdvanceSlowlyAsync(TimeSpan.FromSeconds(5));
        _batcher.Publish(Board(ClubB));
        _batches.Should().BeEmpty();

        await _clock.EventuallyAsync(() => !_batches.IsEmpty, TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(500));

        var batch = _batches.Should().ContainSingle().Subject;
        batch.Select(e => e.ClubId).Should().Equal(ClubA, ClubB);
        batch.Select(e => e.Order.Sequence).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task TheWindow_DoesNotSlide()
    {
        _batcher.Publish(Board(ClubA));
        for (var i = 0; i < 4; i++)
        {
            await _clock.AdvanceSlowlyAsync(TimeSpan.FromSeconds(3));
            _batcher.Publish(Board(ClubB));
        }

        // 12 s in: the first batch went at 10 s, despite the steady trickle.
        await _clock.EventuallyAsync(() => !_batches.IsEmpty, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(200));
        _batches.First().Should().HaveCount(4);
    }

    [Fact]
    public async Task AFailedBatch_AsksOnceForARefreshOfItsClubs()
    {
        _sender.Send(Arg.Any<ApplyClubEventsCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _batches.Enqueue(call.Arg<ApplyClubEventsCommand>().Events);
            return false;
        });

        _batcher.Publish(Board(ClubA));
        _batcher.Publish(Board(ClubB));
        await _clock.EventuallyAsync(() => _batches.Count == 2, TimeSpan.FromSeconds(25), TimeSpan.FromMilliseconds(500));

        _batches.Last().Should().AllBeOfType<ClubRefreshRequested>()
            .And.AllSatisfy(e => ((ClubRefreshRequested)e).Reason.Should().Be(ClubRefreshReason.BatchRetry));
        _batches.Last().Select(e => e.ClubId).Should().BeEquivalentTo([ClubA, ClubB]);

        // The retry failed too: that is left to the polling.
        await _clock.AdvanceSlowlyAsync(TimeSpan.FromSeconds(30));
        _batches.Should().HaveCount(2);
    }

    [Fact]
    public async Task AThrowingBatch_DoesNotStopTheBatcher()
    {
        _sender.Send(Arg.Any<ApplyClubEventsCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"), call =>
            {
                _batches.Enqueue(call.Arg<ApplyClubEventsCommand>().Events);
                return true;
            });

        _batcher.Publish(Board(ClubA));

        await _clock.EventuallyAsync(() => _batches.Count == 1, TimeSpan.FromSeconds(25), TimeSpan.FromMilliseconds(500));
        _batches.Single().Should().ContainSingle().Which.Should().BeOfType<ClubRefreshRequested>();
        _run!.IsCompleted.Should().BeFalse();
    }
}

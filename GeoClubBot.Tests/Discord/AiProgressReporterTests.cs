using Configuration;
using FluentAssertions;
using GeoClubBot.Discord.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using UseCases.UseCases.AI.Progress;
using Xunit;

namespace GeoClubBot.Tests.Discord;

/// <summary>When the status reply is posted, edited and removed (#481).</summary>
public sealed class AiProgressReporterTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ShowsTheFirstStepAtOnce_ThenAtMostOneEditPerInterval_AndRemovesItWhenDone()
    {
        var time = new FakeTimeProvider();
        var display = new RecordingDisplay();
        var log = new AiProgressLog();
        var searching = log.Begin(AiProgressKind.SearchingGuides);

        var reporter = Start(log, display, time, showAfter: 0, interval: 2);

        await WaitUntilAsync(() => display.Shown.Count == 1);
        display.Shown[0].Should().Be("-# ⏳ Searching the guides…");

        searching.Dispose();
        log.Begin(AiProgressKind.Thinking);
        await Task.Delay(50);
        display.Shown.Should().HaveCount(1, "the interval has not passed");

        await WaitUntilAsync(() => display.Shown.Count == 2, () => time.Advance(TimeSpan.FromSeconds(2)));
        display.Shown[1].Should().Be("-# ✓ Searching the guides\n-# ⏳ Thinking…", "both changes are shown in one edit");

        await reporter!.DisposeAsync();
        await reporter.DisposeAsync();

        display.Removed.Should().Be(1);
        log.Begin(AiProgressKind.Posting);
        time.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(50);
        display.Shown.Should().HaveCount(2, "nothing is shown once the answer is done");
    }

    [Fact]
    public async Task ShowsNothing_ForAnAnswerDoneBeforeItsDelay()
    {
        var time = new FakeTimeProvider();
        var display = new RecordingDisplay();
        var log = new AiProgressLog();
        log.Begin(AiProgressKind.Thinking);

        var reporter = Start(log, display, time, showAfter: 5, interval: 2);
        time.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50);
        await reporter!.DisposeAsync();

        display.Shown.Should().BeEmpty();
    }

    [Fact]
    public async Task ShowsTheSteps_OnceTheDelayHasPassed()
    {
        var time = new FakeTimeProvider();
        var display = new RecordingDisplay();
        var log = new AiProgressLog();
        log.Begin(AiProgressKind.Thinking);

        var reporter = Start(log, display, time, showAfter: 5, interval: 2);
        await WaitUntilAsync(() => display.Shown.Count == 1, () => time.Advance(TimeSpan.FromSeconds(1)));
        await reporter!.DisposeAsync();

        display.Shown.Should().Equal("-# ⏳ Thinking…");
    }

    [Fact]
    public async Task NeverCostsTheAnswer_WhenTheDisplayFails()
    {
        var time = new FakeTimeProvider();
        var display = new RecordingDisplay { Fails = true };
        var log = new AiProgressLog();
        log.Begin(AiProgressKind.Thinking);

        var reporter = Start(log, display, time, showAfter: 0, interval: 2);
        await WaitUntilAsync(() => display.Attempts >= 1);
        var act = async () => await reporter!.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void ShowsNothing_WhenTurnedOff()
    {
        var reporter = AiProgressReporter.Start(
            new AiProgressLog(), new RecordingDisplay(), new AiProgressConfiguration { Enabled = false },
            TimeProvider.System, NullLogger.Instance);

        reporter.Should().BeNull();
    }

    private static AiProgressReporter? Start(AiProgressLog log, IAiProgressDisplay display, TimeProvider time, double showAfter, double interval) =>
        AiProgressReporter.Start(
            log, display,
            new AiProgressConfiguration { ShowAfterSeconds = showAfter, MinEditIntervalSeconds = interval },
            time, NullLogger.Instance);

    private static async Task WaitUntilAsync(Func<bool> done, Action? nudge = null)
    {
        var until = DateTime.UtcNow + Patience;
        while (!done())
        {
            DateTime.UtcNow.Should().BeBefore(until, "the reporter should have got there by now");
            nudge?.Invoke();
            await Task.Delay(10);
        }
    }

    private sealed class RecordingDisplay : IAiProgressDisplay
    {
        private readonly Lock _gate = new();
        private readonly List<string> _shown = [];

        public bool Fails { get; init; }

        public int Attempts { get; private set; }

        public int Removed { get; private set; }

        public IReadOnlyList<string> Shown
        {
            get
            {
                lock (_gate)
                {
                    return [.. _shown];
                }
            }
        }

        public Task ShowAsync(string text)
        {
            lock (_gate)
            {
                Attempts++;
                if (Fails)
                {
                    throw new InvalidOperationException("Missing Permissions");
                }

                _shown.Add(text);
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync()
        {
            Removed++;
            return Task.CompletedTask;
        }
    }
}

using FluentAssertions;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Utilities;

public sealed class KeyedAsyncLockTests
{
    [Fact]
    public async Task Acquire_SerialisesCallersSharingAKey()
    {
        var keyedLock = new KeyedAsyncLock<string>();
        var concurrent = 0;
        var observedMax = 0;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            using (await keyedLock.AcquireAsync("same"))
            {
                var current = Interlocked.Increment(ref concurrent);
                observedMax = Math.Max(observedMax, current);
                await Task.Delay(5);
                Interlocked.Decrement(ref concurrent);
            }
        }));

        observedMax.Should().Be(1);
    }

    [Fact]
    public async Task Acquire_DoesNotBlockDifferentKeys()
    {
        // Two conversations have no reason to wait on each other; a single global lock made every
        // user queue behind every other, which is the behaviour this type replaces.
        var keyedLock = new KeyedAsyncLock<string>();

        using var first = await keyedLock.AcquireAsync("a");
        var second = keyedLock.AcquireAsync("b");

        var finished = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(2)));

        finished.Should().Be(second, "a different key must not wait on this one");
        (await second).Dispose();
    }

    [Fact]
    public async Task Acquire_ReleasesTrackingOnceAKeyIsIdle()
    {
        // Without reference counting the map would grow by one semaphore per conversation ever seen.
        var keyedLock = new KeyedAsyncLock<string>();

        using (await keyedLock.AcquireAsync("transient"))
        {
            keyedLock.TrackedKeyCount.Should().Be(1);
        }

        keyedLock.TrackedKeyCount.Should().Be(0);
    }

    [Fact]
    public async Task Acquire_KeepsTheKeyAlive_WhileSomeoneIsStillQueued()
    {
        var keyedLock = new KeyedAsyncLock<string>();

        var first = await keyedLock.AcquireAsync("busy");
        var queued = keyedLock.AcquireAsync("busy");

        keyedLock.TrackedKeyCount.Should().Be(1);

        first.Dispose();
        (await queued).Dispose();

        keyedLock.TrackedKeyCount.Should().Be(0);
    }

    [Fact]
    public async Task Acquire_StopsWaiting_WhenCancelled()
    {
        var keyedLock = new KeyedAsyncLock<string>();
        using var held = await keyedLock.AcquireAsync("held");

        using var cancellation = new CancellationTokenSource();
        var waiting = keyedLock.AcquireAsync("held", cancellation.Token);
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var keyedLock = new KeyedAsyncLock<string>();
        var releaser = await keyedLock.AcquireAsync("key");

        releaser.Dispose();
        releaser.Dispose();

        // A double release would otherwise let two callers into the same key at once.
        using var reacquired = await keyedLock.AcquireAsync("key");
        keyedLock.TrackedKeyCount.Should().Be(1);
    }
}

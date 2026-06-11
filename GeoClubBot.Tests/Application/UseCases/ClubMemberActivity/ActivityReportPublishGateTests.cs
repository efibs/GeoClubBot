using FluentAssertions;
using UseCases.UseCases.ClubMemberActivity;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class ActivityReportPublishGateTests
{
    [Fact]
    public async Task AcquireAsync_BlocksSecondCaller_UntilFirstHandleDisposed()
    {
        var gate = new ActivityReportPublishGate();

        var first = await gate.AcquireAsync();

        var second = gate.AcquireAsync();
        second.IsCompleted.Should().BeFalse("the gate is already held by the first caller");

        await first.DisposeAsync();

        // Releasing the first handle lets the second caller through.
        var secondHandle = await second.WaitAsync(TimeSpan.FromSeconds(5));
        await secondHandle.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_SerializesConcurrentSends_NoInterleaving()
    {
        var gate = new ActivityReportPublishGate();
        var log = new List<string>();

        // Two "clubs" each emit an enter/exit pair while holding the gate. Correct serialization
        // means we never see one club's enter sandwiched between another club's enter and exit.
        async Task PublishAsync(string club)
        {
            await using (await gate.AcquireAsync())
            {
                lock (log)
                {
                    log.Add($"{club}:enter");
                }

                await Task.Yield();
                await Task.Delay(10);

                lock (log)
                {
                    log.Add($"{club}:exit");
                }
            }
        }

        await Task.WhenAll(PublishAsync("A"), PublishAsync("B"));

        // Each enter must be immediately followed by the same club's exit.
        for (var i = 0; i < log.Count; i += 2)
        {
            log[i].Should().EndWith(":enter");
            log[i + 1].Should().Be(log[i].Replace(":enter", ":exit"));
        }
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent_DoesNotOverRelease()
    {
        var gate = new ActivityReportPublishGate();

        var handle = await gate.AcquireAsync();
        await handle.DisposeAsync();
        await handle.DisposeAsync();

        // A double dispose must not leave the semaphore over-released (which would let two callers
        // hold the gate at once). Acquire twice: the second must block until the first releases.
        var first = await gate.AcquireAsync();
        var second = gate.AcquireAsync();
        second.IsCompleted.Should().BeFalse();

        await first.DisposeAsync();
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Should().NotBeNull();
    }
}

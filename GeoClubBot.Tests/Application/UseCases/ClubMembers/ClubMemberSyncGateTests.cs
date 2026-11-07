using FluentAssertions;
using UseCases.UseCases.ClubMembers;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMembers;

public sealed class ClubMemberSyncGateTests
{
    [Fact]
    public async Task ASecondWriter_WaitsUntilTheFirstReleases()
    {
        var gate = new ClubMemberSyncGate();
        // Acquired in a flow of its own: a writer started from the holding flow would count as nested.
        var first = await Task.Run(() => gate.AcquireAsync());

        var second = Task.Run(() => gate.AcquireAsync());
        await Task.Delay(50);
        second.IsCompleted.Should().BeFalse();

        await first.DisposeAsync();
        await using var secondHandle = await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AcquiringAgainFromTheHoldingFlow_Throws_InsteadOfDeadlocking()
    {
        var gate = new ClubMemberSyncGate();
        await using var held = await gate.AcquireAsync();

        var reenter = () => gate.AcquireAsync();

        await reenter.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AFlowStartedWhileHolding_IsAlsoRefused()
    {
        var gate = new ClubMemberSyncGate();
        await using var held = await gate.AcquireAsync();

        // Like SyncClubsHandler's per-club branches, which run inside the held region.
        var nested = Task.Run(async () => await gate.AcquireAsync());

        await nested.Invoking(t => t).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task TheSameFlow_CanAcquireAgain_AfterReleasing()
    {
        var gate = new ClubMemberSyncGate();

        var first = await gate.AcquireAsync();
        await first.DisposeAsync();

        await using var second = await gate.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ACancelledWait_LeavesTheFlowFreeToTryAgain()
    {
        var gate = new ClubMemberSyncGate();
        var otherFlow = await Task.Run(() => gate.AcquireAsync());

        using var cancellation = new CancellationTokenSource();
        var wait = gate.AcquireAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await wait.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();

        await otherFlow.DisposeAsync();
        await using var retry = await gate.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposingTwice_ReleasesOnce()
    {
        var gate = new ClubMemberSyncGate();
        var first = await gate.AcquireAsync();
        await first.DisposeAsync();
        await first.DisposeAsync();

        var second = await Task.Run(() => gate.AcquireAsync());
        var third = Task.Run(() => gate.AcquireAsync());
        await Task.Delay(50);

        third.IsCompleted.Should().BeFalse("a second dispose must not have freed a second slot");
        await second.DisposeAsync();
        await using var thirdHandle = await third.WaitAsync(TimeSpan.FromSeconds(5));
    }
}

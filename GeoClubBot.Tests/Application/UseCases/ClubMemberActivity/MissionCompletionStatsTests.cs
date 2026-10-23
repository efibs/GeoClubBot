using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using UseCases.UseCases.ClubMemberActivity;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

/// <summary>
/// Unit tests for <see cref="MissionCompletionStats"/>: the average covers completed claims made
/// within the period only, and claims left unfinished are counted apart instead of disappearing
/// (issue #402).
/// </summary>
public sealed class MissionCompletionStatsTests
{
    private const string UserId = "user-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset From = Now.AddDays(-14);

    [Fact]
    public void Compute_AveragesTheCompletedClaims()
    {
        var times = MissionCompletionStats.Compute(
            [
                MissionClaims.Completed(UserId, Now.AddDays(-12), TimeSpan.FromHours(2)),
                MissionClaims.Completed(UserId, Now.AddDays(-3), TimeSpan.FromHours(10))
            ],
            From, Now);

        times.CompletedCount.Should().Be(2);
        times.AverageCompletionTime.Should().Be(TimeSpan.FromHours(6));
        times.AbandonedCount.Should().Be(0);
        times.OpenClaimAge.Should().BeNull();
    }

    [Fact]
    public void Compute_OnlyAveragesClaimsMadeWithinThePeriod()
    {
        var times = MissionCompletionStats.Compute(
            [
                MissionClaims.Completed(UserId, Now.AddDays(-3), TimeSpan.FromHours(4)),
                MissionClaims.Completed(UserId, Now.AddDays(-15), TimeSpan.FromHours(40)),
                MissionClaims.Unfinished(UserId, Now.AddDays(-16), periodEnd: Now.AddDays(-13))
            ],
            From, Now);

        times.CompletedCount.Should().Be(1);
        times.AverageCompletionTime.Should().Be(TimeSpan.FromHours(4));
        times.AbandonedCount.Should().Be(0, "the abandoned claim was made before the period");
    }

    [Theory]
    [InlineData(7, 1)]
    [InlineData(14, 2)]
    public void Compute_FollowsThePeriod(int periodDays, int expectedCompleted)
    {
        var times = MissionCompletionStats.Compute(
            [
                MissionClaims.Completed(UserId, Now.AddDays(-3), TimeSpan.FromHours(4)),
                MissionClaims.Completed(UserId, Now.AddDays(-10), TimeSpan.FromHours(8))
            ],
            Now.AddDays(-periodDays), Now);

        times.CompletedCount.Should().Be(expectedCompleted);
    }

    [Fact]
    public void Compute_CountsClaimsWhoseWeekEndedUnfinished_ApartFromTheAverage()
    {
        var times = MissionCompletionStats.Compute(
            [
                MissionClaims.Completed(UserId, Now.AddDays(-5), TimeSpan.FromHours(4)),
                MissionClaims.Unfinished(UserId, Now.AddDays(-4), periodEnd: Now.AddDays(-1))
            ],
            From, Now);

        times.CompletedCount.Should().Be(1);
        times.AverageCompletionTime.Should().Be(TimeSpan.FromHours(4));
        times.AbandonedCount.Should().Be(1);
        times.OpenClaimAge.Should().BeNull("the board week is over, so the claim is no longer held");
    }

    [Fact]
    public void Compute_ReportsTheOpenClaim_EvenWhenItWasMadeBeforeThePeriod()
    {
        var times = MissionCompletionStats.Compute(
            [MissionClaims.Unfinished(UserId, Now.AddDays(-2), periodEnd: Now.AddDays(1))],
            Now.AddDays(-1), Now);

        times.OpenClaimAge.Should().Be(TimeSpan.FromDays(2));
        times.CompletedCount.Should().Be(0);
        times.AverageCompletionTime.Should().BeNull();
        times.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Compute_IsEmpty_WithoutClaims()
    {
        MissionCompletionStats.Compute([], From, Now).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ReadFrom_ReachesABoardWeekBeforeThePeriod_ForOpenClaims()
    {
        MissionCompletionStats.ReadFrom(From).Should().Be(Now.AddDays(-21));
    }
}

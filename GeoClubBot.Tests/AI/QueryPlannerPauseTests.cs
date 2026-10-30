using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// After the query planner runs out of time it sits out a while, so a slow model at the head of the
/// chain does not make every question wait the whole timeout for nothing.
/// </summary>
public sealed class QueryPlannerPauseTests
{
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void IsNotPaused_UntilTold()
    {
        var pause = new QueryPlannerPause(_time);

        pause.IsPaused.Should().BeFalse();
        pause.ResumesAt.Should().BeNull();
    }

    [Fact]
    public void PauseFor_SkipsThePlanner_UntilThePauseHasPassed()
    {
        var pause = new QueryPlannerPause(_time);

        pause.PauseFor(TimeSpan.FromMinutes(30));

        pause.ResumesAt.Should().Be(_time.Now.AddMinutes(30));
        _time.Now += TimeSpan.FromMinutes(29);
        pause.IsPaused.Should().BeTrue();
        _time.Now += TimeSpan.FromMinutes(1);
        pause.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void PauseFor_ALaterTimeout_PausesFromThen()
    {
        var pause = new QueryPlannerPause(_time);
        pause.PauseFor(TimeSpan.FromMinutes(30));

        _time.Now += TimeSpan.FromMinutes(40);
        pause.PauseFor(TimeSpan.FromMinutes(30));

        pause.ResumesAt.Should().Be(_time.Now.AddMinutes(30));
    }

    [Fact]
    public void PauseFor_Nothing_PausesNothing()
    {
        var pause = new QueryPlannerPause(_time);

        pause.PauseFor(TimeSpan.Zero);

        pause.IsPaused.Should().BeFalse("a pause of zero minutes switches the pause off");
    }
}

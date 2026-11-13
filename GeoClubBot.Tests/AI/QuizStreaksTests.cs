using FluentAssertions;
using UseCases.UseCases.AI.Quiz;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class QuizStreaksTests
{
    [Fact]
    public void CountsAStreak_ItsBest_AndTheTally_PerMember()
    {
        var streaks = new QuizStreaks();

        streaks.Record(1, right: true).Should().Be(new QuizScore(1, 1, 1, 1));
        streaks.Record(1, right: true).Should().Be(new QuizScore(2, 2, 2, 2));
        streaks.Record(1, right: false).Should().Be(new QuizScore(0, 2, 2, 3));
        streaks.Record(1, right: true).Should().Be(new QuizScore(1, 2, 3, 4));

        streaks.Record(2, right: false).Should().Be(new QuizScore(0, 0, 0, 1));
        streaks.Read(3).Should().Be(QuizScore.None);
    }
}

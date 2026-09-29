using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.Application.OutputPorts;

/// <summary>
/// The classifier is the one place that knows why a club activity awarded XP. It matters most for
/// the 20 XP awards - the daily challenge / duel and a board mission - which are indistinguishable
/// by amount and only separable by GeoGuessr's activity type.
/// </summary>
public sealed class ClubActivityKindClassifierTests
{
    private readonly ClubActivityKindClassifier _classifier = ClubActivities.Classifier();

    [Theory]
    [InlineData(1, ClubXpActivityKind.DailyMission)]
    [InlineData(2, ClubXpActivityKind.WeeklyMission)]
    [InlineData(3, ClubXpActivityKind.ClubChallengePlayed)]
    [InlineData(4, ClubXpActivityKind.DailyChallengeOrDuel)]
    [InlineData(5, ClubXpActivityKind.BoardMission)]
    [InlineData(6, ClubXpActivityKind.BoardClearBonus)]
    public void Classify_MapsTheFeedsActivityType(int type, ClubXpActivityKind expected)
    {
        var activity = new ReadClubActivitiesItemDto
        {
            UserId = "u1",
            Type = type,
            XpReward = 20,
            RecordedAt = DateTimeOffset.UtcNow
        };

        _classifier.Classify(activity).Should().Be(expected);
    }

    [Fact]
    public void Classify_SeparatesTheAwardsThatShareTheSameXpAmount()
    {
        var mission = ClubActivities.BoardMission("u1");
        var challenge = ClubActivities.Challenge("u1");

        mission.XpReward.Should().Be(challenge.XpReward, "the amount alone cannot tell them apart");

        _classifier.IsBoardMission(mission).Should().BeTrue();
        _classifier.IsDailyChallenge(mission).Should().BeFalse();

        _classifier.IsDailyChallenge(challenge).Should().BeTrue();
        _classifier.IsBoardMission(challenge).Should().BeFalse();
    }

    [Fact]
    public void Classify_RecognisesTheBoardClearBonus()
    {
        var bonus = ClubActivities.BoardBonus("u1");

        _classifier.IsBoardClearBonus(bonus).Should().BeTrue();
        _classifier.IsBoardMission(bonus).Should().BeFalse();
    }

    [Fact]
    public void Classify_ReturnsUnknown_ForATypeGeoGuessrHasNotUsedBefore()
    {
        var activity = new ReadClubActivitiesItemDto
        {
            UserId = "u1",
            Type = 99,
            XpReward = 20,
            RecordedAt = DateTimeOffset.UtcNow
        };

        _classifier.Classify(activity).Should().Be(ClubXpActivityKind.Unknown);
        _classifier.IsBoardMission(activity).Should().BeFalse();
        _classifier.IsDailyChallenge(activity).Should().BeFalse();
    }

    [Theory]
    // Without a type and without a configured fallback the amount says nothing: several sources
    // are worth 20 XP since the board.
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Classify_ReturnsUnknown_ForUntypedEntries_ByDefault(int xpReward)
    {
        _classifier.Classify(ClubActivities.Untyped("u1", xpReward)).Should().Be(ClubXpActivityKind.Unknown);
    }

    [Fact]
    public void Classify_UsesTheConfiguredFallback_ForUntypedEntries()
    {
        var classifier = ClubActivities.Classifier(new ClubXpConfiguration
        {
            UntypedXpFallback = new Dictionary<int, string> { [100] = "BoardClearBonus", [20] = "dailychallengeorduel" }
        });

        classifier.Classify(ClubActivities.Untyped("u1", 100)).Should().Be(ClubXpActivityKind.BoardClearBonus);
        classifier.Classify(ClubActivities.Untyped("u1", 20)).Should().Be(ClubXpActivityKind.DailyChallengeOrDuel);
        classifier.Classify(ClubActivities.Untyped("u1", 150)).Should().Be(ClubXpActivityKind.Unknown);
    }

    [Fact]
    public void Constructor_Throws_WhenTheFallbackNamesAnUnknownKind()
    {
        var act = () => ClubActivities.Classifier(new ClubXpConfiguration
        {
            UntypedXpFallback = new Dictionary<int, string> { [20] = "DailyMisson" }
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*DailyMisson*");
    }

    [Fact]
    public void Classify_PrefersTheType_EvenWhenTheAmountSuggestsOtherwise()
    {
        // A typed entry is authoritative: should GeoGuessr retune the rewards, the type still holds.
        var oddlyPricedMission = ClubActivities.Of("u1", ClubXpActivityKind.BoardMission, xpReward: 100);
        var classifier = ClubActivities.Classifier(new ClubXpConfiguration
        {
            UntypedXpFallback = new Dictionary<int, string> { [100] = "BoardClearBonus" }
        });

        classifier.Classify(oddlyPricedMission).Should().Be(ClubXpActivityKind.BoardMission);
        classifier.IsBoardClearBonus(oddlyPricedMission).Should().BeFalse();
    }
}

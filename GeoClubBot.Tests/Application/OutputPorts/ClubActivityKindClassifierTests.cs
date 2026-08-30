using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.Application.OutputPorts;

/// <summary>
/// The classifier is the one place that knows why a club activity awarded XP. It matters most for
/// the two 20 XP awards - the daily mission and the daily challenge / duel win - which are
/// indistinguishable by amount and only separable by GeoGuessr's activity type.
/// </summary>
public sealed class ClubActivityKindClassifierTests
{
    private readonly ClubActivityKindClassifier _classifier = ClubActivities.Classifier();

    [Theory]
    [InlineData(1, ClubXpActivityKind.DailyMission)]
    [InlineData(2, ClubXpActivityKind.WeeklyMission)]
    [InlineData(3, ClubXpActivityKind.ClubChallengePlayed)]
    [InlineData(4, ClubXpActivityKind.DailyChallengeOrDuel)]
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
    public void Classify_SeparatesTheTwoAwardsThatShareTheSameXpAmount()
    {
        var mission = ClubActivities.Mission("u1");
        var challenge = ClubActivities.Challenge("u1");

        mission.XpReward.Should().Be(challenge.XpReward, "the amount alone cannot tell them apart");

        _classifier.IsDailyMission(mission).Should().BeTrue();
        _classifier.IsDailyChallenge(mission).Should().BeFalse();

        _classifier.IsDailyChallenge(challenge).Should().BeTrue();
        _classifier.IsDailyMission(challenge).Should().BeFalse();
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
        _classifier.IsDailyMission(activity).Should().BeFalse();
        _classifier.IsDailyChallenge(activity).Should().BeFalse();
    }

    [Theory]
    // Without a type the amount is all there is. The daily mission is the older 20 XP source, so
    // that is what an untyped 20 XP entry is read as - reproducing the bot's historical behaviour.
    [InlineData(20, ClubXpActivityKind.DailyMission)]
    [InlineData(1000, ClubXpActivityKind.WeeklyMission)]
    [InlineData(150, ClubXpActivityKind.Unknown)]
    public void Classify_FallsBackToTheXpAmount_WhenTheEntryCarriesNoType(int xpReward, ClubXpActivityKind expected)
    {
        _classifier.Classify(ClubActivities.Untyped("u1", xpReward)).Should().Be(expected);
    }

    [Fact]
    public void Classify_UsesTheConfiguredAmounts_ForTheUntypedFallback()
    {
        var classifier = ClubActivities.Classifier(new ClubXpConfiguration
        {
            DailyMissionXpReward = 25,
            WeeklyMissionXpReward = 500
        });

        classifier.Classify(ClubActivities.Untyped("u1", 25)).Should().Be(ClubXpActivityKind.DailyMission);
        classifier.Classify(ClubActivities.Untyped("u1", 500)).Should().Be(ClubXpActivityKind.WeeklyMission);
        classifier.Classify(ClubActivities.Untyped("u1", 20)).Should().Be(ClubXpActivityKind.Unknown);
    }

    [Fact]
    public void Classify_PrefersTheType_EvenWhenTheAmountSuggestsOtherwise()
    {
        // A typed entry is authoritative: should GeoGuessr retune the rewards, the type still holds.
        var oddlyPricedMission = ClubActivities.Of("u1", ClubXpActivityKind.DailyMission, xpReward: 1000);

        _classifier.Classify(oddlyPricedMission).Should().Be(ClubXpActivityKind.DailyMission);
        _classifier.IsWeeklyMission(oddlyPricedMission).Should().BeFalse();
    }
}

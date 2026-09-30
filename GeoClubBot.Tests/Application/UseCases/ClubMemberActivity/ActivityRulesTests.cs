using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Options;
using UseCases.UseCases.ClubMemberActivity.Rules;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class ActivityRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 11, 0, 0, TimeSpan.Zero);

    private static ActivityCheckerConfiguration Defaults() => new ActivityCheckerConfigurationBuilder()
        .WithMinXp(0)
        .WithRequirements(("DailyChallengeOrDuel", 6), ("BoardMission", 2))
        .WithRuleXp(["BoardClearBonus"], ("BoardMission", 3))
        .BuildOptions().Value;

    private static GeoGuessrClubEntry Club(Action<GeoGuessrClubEntry>? configure = null)
    {
        var entry = new GeoGuessrClubEntry { ClubId = Guid.NewGuid(), NcfaToken = "x", IsMain = true };
        configure?.Invoke(entry);
        return entry;
    }

    [Fact]
    public void Resolve_TakesTheGlobalRules_WhenTheClubHasNoOverrides()
    {
        var rules = ActivityRules.Resolve(Defaults(), Club());

        rules.MinCounts.Should().BeEquivalentTo(new Dictionary<ClubXpActivityKind, int>
        {
            [ClubXpActivityKind.DailyChallengeOrDuel] = 6,
            [ClubXpActivityKind.BoardMission] = 2
        });
        rules.ExcludedFromRuleXp.Should().BeEquivalentTo([ClubXpActivityKind.BoardClearBonus]);
        rules.MaxCountedPerWeek.Should().ContainKey(ClubXpActivityKind.BoardMission).WhoseValue.Should().Be(3);
        rules.Describe().Should().Be("streak 6 · missions 2");
    }

    [Fact]
    public void Resolve_MergesClubOverridesKeyByKey_AndDropsZeroRequirements()
    {
        var rules = ActivityRules.Resolve(Defaults(), Club(c =>
        {
            c.Requirements = new Dictionary<string, int> { ["boardmission"] = 0, ["DailyChallengeOrDuel"] = 5 };
            c.MinXP = 100;
            c.RuleXpExcludedKinds = [];
        }));

        rules.MinCounts.Should().BeEquivalentTo(new Dictionary<ClubXpActivityKind, int>
        {
            [ClubXpActivityKind.DailyChallengeOrDuel] = 5
        });
        rules.MinRuleXp.Should().Be(100);
        rules.ExcludedFromRuleXp.Should().BeEmpty();
        rules.Describe().Should().Be("streak 5 · 100XP");
    }

    [Fact]
    public void Resolve_Throws_NamingTheUnknownKind()
    {
        var club = Club(c => c.Requirements = new Dictionary<string, int> { ["BoardMisions"] = 2 });

        var act = () => ActivityRules.Resolve(Defaults(), club);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Requirements*BoardMisions*");
    }

    [Fact]
    public void OptionsValidator_FailsStartUp_ForAnUnknownKindInAnyClub()
    {
        var geoGuessr = Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "x",
            ActivityNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = [Club(), Club(c => { c.IsMain = false; c.RuleXpExcludedKinds = ["Bonus"]; })]
        });

        var result = new ActivityRulesOptionsValidator(geoGuessr).Validate(null, Defaults());

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("'Bonus'");
    }

    [Fact]
    public void Evaluate_CountsTheEarliestCappedEntries_AndDropsExcludedKinds()
    {
        var rules = ActivityRules.Resolve(Defaults(), Club());
        var entries = new List<ClubActivityEntry>
        {
            new(ClubXpActivityKind.DailyChallengeOrDuel, 20, Start.AddHours(1)),
            new(ClubXpActivityKind.BoardMission, 20, Start.AddHours(2)),
            new(ClubXpActivityKind.BoardMission, 20, Start.AddHours(3)),
            new(ClubXpActivityKind.BoardMission, 20, Start.AddHours(4)),
            new(ClubXpActivityKind.BoardMission, 20, Start.AddHours(5)),
            new(ClubXpActivityKind.BoardClearBonus, 100, Start.AddHours(5))
        };

        var evaluation = ActivityRuleEvaluator.Evaluate(entries, rules, targetFactor: 1);

        evaluation.RawXp.Should().Be(200);
        evaluation.RuleXp.Should().Be(20 + 3 * 20);
        evaluation.CountOf(ClubXpActivityKind.BoardMission).Should().Be(4, "the cap limits XP, not the count");
        evaluation.Requirements.Should().BeEquivalentTo(new[]
        {
            new ActivityRequirementResult(ClubXpActivityKind.DailyChallengeOrDuel, 1, 6, 6),
            new ActivityRequirementResult(ClubXpActivityKind.BoardMission, 4, 2, 2)
        });
        evaluation.AllMet.Should().BeFalse();
    }

    [Theory]
    [InlineData(1.0, 6)]
    [InlineData(0.5, 3)]
    [InlineData(0.99, 5)]
    [InlineData(0.0, 0)]
    public void ScaleTarget_RoundsDown(double factor, int expected)
    {
        ActivityRuleEvaluator.ScaleTarget(6, factor).Should().Be(expected);
    }
}

using FluentAssertions;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.CountryChallenges.Configuration;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

/// <summary>
/// The file is edited by hand, so the resolver is where mistakes are caught. These tests pin the
/// inheritance order — built-in default, top of the file, challenge, pool entry — and that every problem
/// is reported with where it is, instead of the file silently doing something other than intended.
/// </summary>
public sealed class CountryChallengePlanResolverTests
{
    // ---- Inheritance ------------------------------------------------------

    [Fact]
    public void Resolve_FillsEverythingFromTheBuiltInDefaults_WhenTheFileOnlyHasAChannelAndChallenges()
    {
        var plan = Plan(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia", "MN")));

        var challenge = plan.Challenges.Should().ContainSingle().Subject;
        challenge.ChannelId.Should().Be(ChannelId);
        challenge.Entry.Should().Be(CountryChallengeDefaults.AnnouncementEntry);
        challenge.Results.Should().Be(CountryChallengeDefaults.Results);
        challenge.Countries.Single().Settings.Should().Be(CountryChallengeDefaults.Settings);
        plan.Leaderboard.Days.Should().BeEquivalentTo([DayOfWeek.Monday]);
        plan.Leaderboard.Season.Should().Be(CountryChallengeDefaults.LeaderboardSeason);
        plan.Announcement.Thread.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Resolve_LetsTheMostSpecificLevelWin_ForEachSettingSeparately()
    {
        var file = File(Pool("Small Country Sunday", DayOfWeek.Sunday,
            Country("Andorra"),
            Country("Malta", settings: new GameSettingsSection { TimeLimit = 30 }))) with
        {
            Settings = new GameSettingsSection { TimeLimit = 120, ForbidMoving = true }
        };
        file.Challenges![0] = file.Challenges[0] with { Settings = new GameSettingsSection { ForbidZooming = true } };

        var countries = Plan(file).Challenges.Single().Countries;

        // Top level: 120 s + no move. Challenge: + no zoom. Malta: 30 s. Nothing else changes.
        countries.Single(c => c.Name == "Andorra").Settings.Should().Be(new GameSettings(120, true, false, true));
        countries.Single(c => c.Name == "Malta").Settings.Should().Be(new GameSettings(30, true, false, true));
    }

    [Fact]
    public void Resolve_OverridesOnlyTheResultsSettingsAChallengeSets()
    {
        var file = File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with
        {
            Results = new ResultsSection { AfterDays = 2, Points = [5, 3, 1], Top = 5 }
        };
        file.Challenges![0] = file.Challenges[0] with { Results = new ChallengeResultsSection { Points = [10], RoleIds = [77] } };

        var results = Plan(file).Challenges.Single().Results;

        results.AfterDays.Should().Be(2);
        results.Top.Should().Be(5);
        results.Points.Should().Equal(10);
        results.RoleIds.Should().Equal(77UL);
        results.HighscoreLimit.Should().Be(5);
    }

    [Fact]
    public void Resolve_UsesTheChallengesOwnMentionsAndChannel_InsteadOfTheDefaults()
    {
        var file = File(
            Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")) with { ChannelId = 9, MentionRoleIds = [3] },
            Fixed("Argentina Friday", DayOfWeek.Friday, Country("Argentina"))) with
        {
            Announcement = new AnnouncementSection { MentionRoleIds = [1, 2] }
        };

        var plan = Plan(file);

        plan.Challenges[0].ChannelId.Should().Be(9UL);
        plan.Challenges[0].MentionRoleIds.Should().Equal(3UL);
        plan.Challenges[1].ChannelId.Should().Be(ChannelId);
        plan.Challenges[1].MentionRoleIds.Should().Equal(1UL, 2UL);
    }

    [Fact]
    public void Resolve_KeepsRoleIdsAsWritten_SoOneRoleCanCoverSeveralPlaces()
    {
        var file = File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with
        {
            Results = new ResultsSection { RoleIds = [7, 7, 7] }
        };

        Plan(file).Challenges.Single().Results.RoleIds.Should().Equal(7UL, 7UL, 7UL);
    }

    [Fact]
    public void Resolve_SchedulesAChallengeOnItsDaysAndOnItsDates()
    {
        var christmas = new DateOnly(2026, 12, 25);
        var plan = Plan(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")) with { Dates = [christmas] });

        plan.ChallengesOn(Monday).Should().ContainSingle();
        plan.ChallengesOn(christmas).Should().ContainSingle("Christmas 2026 is a Friday but listed as a date");
        plan.ChallengesOn(Friday).Should().BeEmpty();
    }

    [Fact]
    public void ResultsFor_FallsBackToTheTopLevelResults_ForAChallengeNoLongerInTheFile()
    {
        var plan = Plan(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")) with
        {
            Results = new ChallengeResultsSection { Points = [9] }
        });

        plan.ResultsFor("MONGOLIA MONDAY").Points.Should().Equal(9);
        plan.ResultsFor("Retired Challenge").Should().Be(plan.Results.Defaults);
    }

    // ---- Disabled challenges ----------------------------------------------

    [Fact]
    public void Resolve_OnlyWarnsAboutADisabledChallenge_SoAnUnfinishedOneCanBeParked()
    {
        var file = File(
            Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")),
            new ChallengeSection { Name = "Japan Wednesday", Enabled = false, Days = [DayOfWeek.Wednesday], Country = new CountrySection { Name = "Japan" } });

        var resolution = CountryChallengePlanResolver.Resolve(file);

        resolution.Errors.Should().BeEmpty();
        resolution.Plan!.Challenges.Should().ContainSingle(c => c.Name == "Mongolia Monday");
        resolution.Plan.Warnings.Should().ContainSingle(w => w.Contains("Japan Wednesday") && w.Contains("MapId is required"));
    }

    [Fact]
    public void Resolve_KeepsAValidDisabledChallengeForItsPendingResults_ButNeverSchedulesIt()
    {
        var file = File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")) with
        {
            Enabled = false,
            Results = new ChallengeResultsSection { Points = [9] }
        });

        var plan = Plan(file);

        plan.ChallengesOn(Monday).Should().BeEmpty();
        plan.ResultsFor("Mongolia Monday").Points.Should().Equal(9);
        plan.Warnings.Should().Contain(w => w.Contains("No challenge is enabled"));
    }

    // ---- Errors -----------------------------------------------------------

    [Fact]
    public void Resolve_ReportsEveryProblemAtOnce_EachWithWhereItIs()
    {
        var file = File(
            Pool("Small Country Sunday", DayOfWeek.Sunday,
                Country("Andorra"),
                new CountrySection { Name = "Malta" },
                Country("Monaco", code: "MCO")),
            new ChallengeSection { Name = "No Days", Country = Country("Chile") });

        var errors = CountryChallengePlanResolver.Resolve(file).Errors;

        errors.Should().BeEquivalentTo(
        [
            "Challenges[0] \"Small Country Sunday\" › Pool[1] \"Malta\": MapId is required.",
            "Challenges[0] \"Small Country Sunday\" › Pool[2] \"Monaco\": Code 'MCO' is not a two-letter country code such as 'MN'.",
            "Challenges[1] \"No Days\": set Days and/or Dates, otherwise the challenge never runs."
        ]);
    }

    [Fact]
    public void Resolve_RejectsTwoChallengesWithTheSameName_EvenIfOneIsDisabled()
    {
        var file = File(
            Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")),
            Fixed("mongolia monday", DayOfWeek.Tuesday, Country("Mongolia")) with { Enabled = false });

        CountryChallengePlanResolver.Resolve(file).Errors
            .Should().ContainSingle(e => e.Contains("Names must be unique"));
    }

    [Fact]
    public void Resolve_RejectsAChallengeWithBothCountryAndPool_OrNeither()
    {
        var file = File(
            Fixed("Both", DayOfWeek.Monday, Country("Mongolia")) with { Pool = [Country("Chile")] },
            new ChallengeSection { Name = "Neither", Days = [DayOfWeek.Tuesday] },
            new ChallengeSection { Name = "Empty pool", Days = [DayOfWeek.Wednesday], Pool = [] });

        var errors = CountryChallengePlanResolver.Resolve(file).Errors;

        errors.Should().HaveCount(3);
        errors[0].Should().Contain("either Country or Pool, not both");
        errors[1].Should().Contain("non-empty Pool");
        errors[2].Should().Contain("non-empty Pool");
    }

    [Fact]
    public void Resolve_ReadsPicks_DefaultingToOne()
    {
        var plan = Plan(
            Pool("Middleweight Saturday", DayOfWeek.Saturday, Country("Peru"), Country("Chile"), Country("Japan")) with { Picks = 2 },
            Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")));

        plan.Challenges.Select(c => c.Picks).Should().Equal(2, 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Resolve_RejectsPicksOutsideThePoolSize(int picks)
    {
        var file = File(Pool("Middleweight Saturday", DayOfWeek.Saturday, Country("Peru"), Country("Chile"), Country("Japan")) with
        {
            Picks = picks
        });

        CountryChallengePlanResolver.Resolve(file).Errors.Should().ContainSingle().Which.Should().Be(
            "Challenges[0] \"Middleweight Saturday\": Picks must be between 1 and the number of countries in the pool (3).");
    }

    [Fact]
    public void Resolve_RejectsPicksOnASingleCountry()
    {
        var file = File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")) with { Picks = 2 });

        CountryChallengePlanResolver.Resolve(file).Errors.Should().ContainSingle(e => e.Contains("Picks needs a Pool"));
    }

    [Fact]
    public void Resolve_RejectsTwoPoolCountriesWithTheSameName()
    {
        var file = File(Pool("Small Country Sunday", DayOfWeek.Sunday, Country("Malta"), Country("MALTA", mapId: "other")));

        CountryChallengePlanResolver.Resolve(file).Errors
            .Should().ContainSingle(e => e.Contains("Pool[1]") && e.Contains("already has a country with this name"));
    }

    [Fact]
    public void Resolve_RejectsAChallengeWithoutAChannel()
    {
        var file = File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with { ChannelId = null };

        CountryChallengePlanResolver.Resolve(file).Errors.Should().BeEquivalentTo(
        [
            "Challenges[0] \"Mongolia Monday\": no channel. Set ChannelId at the top of the file or on the challenge.",
            "Leaderboard: no channel. Set ChannelId at the top of the file or in Leaderboard."
        ]);
    }

    [Fact]
    public void Resolve_DoesNotNeedALeaderboardChannel_WhenTheLeaderboardIsOff()
    {
        var file = File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")) with { ChannelId = 7 }) with
        {
            ChannelId = null,
            Leaderboard = new LeaderboardSection { Enabled = false }
        };

        CountryChallengePlanResolver.Resolve(file).Errors.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ReportsAMistakeAtTheTopOfTheFileOnce_NotOncePerChallengeInheritingIt()
    {
        var file = File(
            Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")),
            Fixed("Argentina Friday", DayOfWeek.Friday, Country("Argentina"))) with
        {
            Settings = new GameSettingsSection { TimeLimit = -1 },
            Results = new ResultsSection { Top = 0, Points = [3, -1] }
        };

        CountryChallengePlanResolver.Resolve(file).Errors.Should().BeEquivalentTo(
        [
            "Settings.TimeLimit must not be negative (0 means no limit).",
            "Results.Top must be between 1 and 25.",
            "Results.Points must not be negative."
        ]);
    }

    [Fact]
    public void Resolve_RejectsATemplateUsingAPlaceholderThatIsNotAvailableThere()
    {
        var file = File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")) with { Entry = "{{name}} {{contry}}" }) with
        {
            Leaderboard = new LeaderboardSection { Message = "{{leaderboard}} {{link}}" }
        };

        var errors = CountryChallengePlanResolver.Resolve(file).Errors;

        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.StartsWith("Leaderboard.Message: {{link}} is not available here."));
        errors.Should().Contain(e => e.StartsWith("Challenges[0] \"Mongolia Monday\" › Entry: {{contry}} is not available here."));
    }

    [Fact]
    public void Resolve_RejectsAnEmptyTemplateAndAZeroId()
    {
        var file = File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with
        {
            Announcement = new AnnouncementSection { Message = " ", MentionRoleIds = [0] }
        };

        CountryChallengePlanResolver.Resolve(file).Errors.Should().BeEquivalentTo(
        [
            "Announcement.Message must not be empty.",
            "Announcement.MentionRoleIds contains 0, which is not a Discord id."
        ]);
    }

    [Fact]
    public void Resolve_ReadsTheThreadSettings()
    {
        var file = File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with
        {
            Announcement = new AnnouncementSection
            {
                Thread = new ThreadSection { Enabled = true, AutoArchive = ThreadAutoArchive.OneWeek }
            }
        };

        var thread = Plan(file).Announcement.Thread;

        thread.Should().Be(new ThreadPlan(true, CountryChallengeDefaults.Thread.Name, ThreadAutoArchive.OneWeek));
    }

    [Fact]
    public void Resolve_NormalisesTheCountryCode()
    {
        var plan = Plan(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia", code: " mn ")));

        plan.Challenges.Single().Countries.Single().Code.Should().Be("MN");
    }
}

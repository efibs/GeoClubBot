using FluentAssertions;
using UseCases.UseCases.CountryChallenges.Configuration;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

/// <summary>
/// Every rule of the file at its limits, each with the path it reports: an admin fixes the file from
/// these messages alone, so a wrong path is as much a bug as a missed mistake.
/// </summary>
public sealed class CountryChallengePlanValidationTests
{
    private const string Name = "Mongolia Monday";
    private const string At = "Challenges[0] \"Mongolia Monday\"";

    private static ChallengeSection Challenge() => Fixed(Name, DayOfWeek.Monday, Country("Mongolia"));

    private static CountryChallengesFile FileOf() => File(Challenge());

    private static string Letters(int count) => new('x', count);

    private static List<int> Numbers(int count) => [.. Enumerable.Range(1, count)];

    private static List<ulong> Ids(int count) => [.. Enumerable.Range(1, count).Select(i => (ulong)i)];

    private static IReadOnlyList<string> ErrorsOf(CountryChallengesFile file) =>
        CountryChallengePlanResolver.Resolve(file).Errors;

    private static IReadOnlyList<string> ErrorsOf(ChallengeSection challenge) => ErrorsOf(File(challenge));

    private static IReadOnlyList<string> CountryErrors(CountrySection country) =>
        ErrorsOf(Fixed(Name, DayOfWeek.Monday, country));

    private static CountryPlan CountryOf(CountrySection country) =>
        Plan(Fixed(Name, DayOfWeek.Monday, country)).Challenges.Single().Countries.Single();

    // ---- The file itself --------------------------------------------------

    [Fact]
    public void AFileWithoutChallenges_IsValid_ButWarns()
    {
        var plan = Plan(new CountryChallengesFile { ChannelId = ChannelId });

        plan.Challenges.Should().BeEmpty();
        plan.Warnings.Should().Equal("No challenge is enabled, so nothing will be posted.");
    }

    [Fact]
    public void AChannelOfZero_AtTheTop_IsNoDiscordId()
    {
        ErrorsOf(FileOf() with { ChannelId = 0 }).Should().Contain("ChannelId is 0, which is not a Discord id.");
    }

    [Theory]
    [InlineData("Announcement.Entry")]
    [InlineData("Announcement.Message")]
    [InlineData("Announcement.Thread.Name")]
    [InlineData("Results.Message")]
    [InlineData("Results.Entry")]
    [InlineData("Leaderboard.Message")]
    public void EachTemplate_ReportsItsOwnPath(string path)
    {
        var file = path switch
        {
            "Announcement.Entry" => FileOf() with { Announcement = new AnnouncementSection { Entry = " " } },
            "Announcement.Message" => FileOf() with { Announcement = new AnnouncementSection { Message = " " } },
            "Announcement.Thread.Name" => FileOf() with { Announcement = new AnnouncementSection { Thread = new ThreadSection { Name = " " } } },
            "Results.Message" => FileOf() with { Results = new ResultsSection { Message = " " } },
            "Results.Entry" => FileOf() with { Results = new ResultsSection { Entry = " " } },
            _ => FileOf() with { Leaderboard = new LeaderboardSection { Message = " " } }
        };

        ErrorsOf(file).Should().Equal($"{path} must not be empty.");
    }

    [Fact]
    public void ATemplateProblem_NamesTheTemplate()
    {
        var errors = ErrorsOf(FileOf() with { Announcement = new AnnouncementSection { Message = "{{nope}} {{challenges}}" } });

        errors.Should().ContainSingle().Which.Should().StartWith("Announcement.Message: ");
    }

    [Theory]
    [InlineData("Announcement.MentionRoleIds")]
    [InlineData("Results.MentionRoleIds")]
    [InlineData("Results.RoleIds")]
    [InlineData("Leaderboard.RoleIds")]
    [InlineData("Leaderboard.MentionRoleIds")]
    public void ARoleOfZero_IsNoDiscordId_WhereverItIsListed(string path)
    {
        var file = path switch
        {
            "Announcement.MentionRoleIds" => FileOf() with { Announcement = new AnnouncementSection { MentionRoleIds = [0] } },
            "Results.MentionRoleIds" => FileOf() with { Results = new ResultsSection { MentionRoleIds = [0] } },
            "Results.RoleIds" => FileOf() with { Results = new ResultsSection { RoleIds = [0] } },
            "Leaderboard.RoleIds" => FileOf() with { Leaderboard = new LeaderboardSection { RoleIds = [0] } },
            _ => FileOf() with { Leaderboard = new LeaderboardSection { MentionRoleIds = [0] } }
        };

        ErrorsOf(file).Should().Equal($"{path} contains 0, which is not a Discord id.");
    }

    [Fact]
    public void TheMentionedRoles_AreKept()
    {
        var plan = Plan(FileOf() with { Results = new ResultsSection { MentionRoleIds = [5] } });

        plan.Results.MentionRoleIds.Should().Equal(5UL);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void ANegativeTimeLimit_IsRefused_ButZeroMeansNoLimit(int limit, bool valid)
    {
        var errors = ErrorsOf(FileOf() with { Settings = new GameSettingsSection { TimeLimit = limit } });

        errors.Should().BeEquivalentTo(valid ? [] : new[] { "Settings.TimeLimit must not be negative (0 means no limit)." });
    }

    // ---- A challenge ------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void AChallenge_NeedsAName(string? name)
    {
        var errors = ErrorsOf(Challenge() with { Name = name });

        errors.Should().Contain("Challenges[0]: Name is required.");
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void AChallengeName_HasAMaximumLength(int length, bool valid)
    {
        var name = Letters(length);

        var errors = ErrorsOf(Challenge() with { Name = name });

        errors.Should().BeEquivalentTo(valid ? [] : new[] { $"Challenges[0] \"{name}\": Name is longer than 100 characters." });
    }

    [Fact]
    public void AChallenge_ReportsItsOwnChannelOfZero()
    {
        ErrorsOf(Challenge() with { ChannelId = 0 }).Should().Equal($"{At} › ChannelId is 0, which is not a Discord id.");
    }

    [Fact]
    public void AChallenge_ReportsItsOwnMentionsOfZero()
    {
        ErrorsOf(Challenge() with { MentionRoleIds = [0] }).Should().Equal($"{At} › MentionRoleIds contains 0, which is not a Discord id.");
    }

    [Fact]
    public void AChallenge_ReportsItsOwnSettings()
    {
        ErrorsOf(Challenge() with { Settings = new GameSettingsSection { TimeLimit = -1 } })
            .Should().Equal($"{At} › Settings.TimeLimit must not be negative (0 means no limit).");
    }

    [Fact]
    public void AChallenge_ReportsItsOwnEntry()
    {
        ErrorsOf(Challenge() with { Entry = " " }).Should().Equal($"{At} › Entry must not be empty.");
    }

    // ---- A country --------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void ACountry_NeedsAName(string? name)
    {
        CountryErrors(Country("x") with { Name = name }).Should().Equal($"{At} › Country: Name is required.");
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void ACountryName_HasAMaximumLength(int length, bool valid)
    {
        CountryErrors(Country(Letters(length), mapId: "map-1")).Should().BeEquivalentTo(
            valid ? [] : new[] { $"{At} › Country: Name is longer than 100 characters." });
    }

    [Theory]
    [InlineData("MN", true)]
    [InlineData("M1", false)]
    [InlineData("M", false)]
    [InlineData("MNO", false)]
    public void ACountryCode_IsTwoLetters(string code, bool valid)
    {
        CountryErrors(Country("Mongolia", code)).Should().BeEquivalentTo(
            valid ? [] : new[] { $"{At} › Country: Code '{code}' is not a two-letter country code such as 'MN'." });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void ACountry_NeedsAMap(string? mapId)
    {
        CountryErrors(Country("Mongolia") with { MapId = mapId }).Should().Equal($"{At} › Country: MapId is required.");
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("a\tb")]
    public void AMapId_HasNoSpaces(string mapId)
    {
        CountryErrors(Country("Mongolia", mapId: mapId)).Should().Equal(
            $"{At} › Country: MapId '{mapId}' is not a GeoGuessr map id (the part after /maps/ in the map's link).");
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void AMapId_HasAMaximumLength(int length, bool valid)
    {
        CountryErrors(Country("Mongolia", mapId: Letters(length))).Should().HaveCount(valid ? 0 : 1);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void AMapName_HasAMaximumLength(int length, bool valid)
    {
        CountryErrors(Country("Mongolia") with { MapName = Letters(length) }).Should().BeEquivalentTo(
            valid ? [] : new[] { $"{At} › Country: MapName is longer than 128 characters." });
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" Big Map ", "Big Map")]
    public void AMapName_IsTrimmed_AndBlankMeansNone(string? written, string? expected)
    {
        CountryOf(Country("Mongolia") with { MapName = written }).MapName.Should().Be(expected);
    }

    [Fact]
    public void ACountryName_AndMapId_AreTrimmed()
    {
        var country = CountryOf(Country(" Mongolia ", mapId: " map-1 "));

        country.Name.Should().Be("Mongolia");
        country.MapId.Should().Be("map-1");
    }

    [Fact]
    public void ACountry_ReportsItsOwnSettings()
    {
        CountryErrors(Country("Mongolia", settings: new GameSettingsSection { TimeLimit = -1 }))
            .Should().Equal($"{At} › Country › Settings.TimeLimit must not be negative (0 means no limit).");
    }

    [Fact]
    public void APoolEntry_ReportsWhereItIs_WithItsNameWhenItHasOne()
    {
        var errors = ErrorsOf(Pool(Name, DayOfWeek.Monday, Country("Malta"), Country("Andorra") with { MapId = null }));

        errors.Should().Equal($"{At} › Pool[1] \"Andorra\": MapId is required.");
    }

    [Fact]
    public void APoolEntryWithoutAName_ReportsItsPosition()
    {
        var errors = ErrorsOf(Pool(Name, DayOfWeek.Monday, Country("Malta"), Country("x") with { Name = " " }));

        errors.Should().Equal($"{At} › Pool[1]: Name is required.");
    }

    [Fact]
    public void AnInvalidPoolEntry_LeavesTheChallengeOutOfThePlan_EvenWhenOnlyWarnedAbout()
    {
        var parked = Pool(Name, DayOfWeek.Monday, Country("Malta"), Country("Andorra") with { MapId = null }) with { Enabled = false };

        var plan = Plan(parked);

        plan.Warnings.Should().Contain(w => w.StartsWith($"{At} › Pool[1]") && w.EndsWith("(the challenge is disabled, so this is only a warning)"));
        plan.DisabledChallenges.Should().BeEmpty();
    }

    [Fact]
    public void ASingleCountryThatIsInvalid_IsOneProblem_NotAnEmptyChallenge()
    {
        var parked = Challenge() with { Enabled = false, Country = Country("x") with { MapId = null } };

        Plan(parked).DisabledChallenges.Should().BeEmpty();
    }

    // ---- Results ----------------------------------------------------------

    private static IReadOnlyList<string> ResultsErrors(ChallengeResultsSection results) =>
        ErrorsOf(Challenge() with { Results = results });

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(365, true)]
    [InlineData(366, false)]
    public void AfterDays_StaysWithinAYear(int days, bool valid)
    {
        ResultsErrors(new ChallengeResultsSection { AfterDays = days }).Should().BeEquivalentTo(
            valid ? [] : new[] { $"{At} › Results.AfterDays must be between 0 and 365." });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void ResultsTop_StaysWithinItsLimits(int top, bool valid)
    {
        ResultsErrors(new ChallengeResultsSection { Top = top }).Should().BeEquivalentTo(
            valid ? [] : new[] { $"{At} › Results.Top must be between 1 and 25." });
    }

    [Theory]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void Points_ListALimitedNumberOfPlaces(int places, bool valid)
    {
        ResultsErrors(new ChallengeResultsSection { Points = Numbers(places) }).Should().BeEquivalentTo(
            valid ? [] : new[] { $"{At} › Results.Points may list at most 25 places." });
    }

    [Theory]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void Roles_ListALimitedNumberOfPlaces(int roles, bool valid)
    {
        ResultsErrors(new ChallengeResultsSection { RoleIds = Ids(roles) }).Should().BeEquivalentTo(
            valid ? [] : new[] { $"{At} › Results.RoleIds may name at most 25 roles." });
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void Points_MayBeZero_ButNeverNegative(int points, bool valid)
    {
        ResultsErrors(new ChallengeResultsSection { Points = [points] }).Should().BeEquivalentTo(
            valid ? [] : new[] { $"{At} › Results.Points must not be negative." });
    }

    [Fact]
    public void AResultsChannelOfZero_IsNoDiscordId()
    {
        ResultsErrors(new ChallengeResultsSection { ChannelId = 0 }).Should().Equal($"{At} › Results.ChannelId is 0, which is not a Discord id.");
    }

    [Fact]
    public void ResultsEntry_AndRoles_OfAChallenge_ReportTheirPaths()
    {
        ResultsErrors(new ChallengeResultsSection { Entry = " ", RoleIds = [0] }).Should().BeEquivalentTo(
            $"{At} › Results.RoleIds contains 0, which is not a Discord id.",
            $"{At} › Results.Entry must not be empty.");
    }

    [Fact]
    public void TheResultsChannel_IsInheritedFromTheTop_AndOverriddenByAChallenge()
    {
        var plan = Plan(File(
            Challenge(),
            Fixed("Own Channel", DayOfWeek.Tuesday, Country("Chile")) with { Results = new ChallengeResultsSection { ChannelId = 9 } }) with
        {
            Results = new ResultsSection { ChannelId = 7 }
        });

        plan.Challenges[0].Results.ChannelId.Should().Be(7);
        plan.Challenges[1].Results.ChannelId.Should().Be(9);
    }

    // ---- Leaderboard ------------------------------------------------------

    [Theory]
    [InlineData("  ", "Leaderboard.Season must not be empty.")]
    public void ASeason_MustBeNamed(string season, string expected)
    {
        ErrorsOf(FileOf() with { Leaderboard = new LeaderboardSection { Season = season } }).Should().Equal(expected);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void ASeasonName_HasAMaximumLength(int length, bool valid)
    {
        ErrorsOf(FileOf() with { Leaderboard = new LeaderboardSection { Season = Letters(length) } }).Should().BeEquivalentTo(
            valid ? [] : new[] { "Leaderboard.Season is longer than 64 characters." });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void LeaderboardTop_StaysWithinItsLimits(int top, bool valid)
    {
        ErrorsOf(FileOf() with { Leaderboard = new LeaderboardSection { Top = top } }).Should().BeEquivalentTo(
            valid ? [] : new[] { "Leaderboard.Top must be between 1 and 50." });
    }

    [Theory]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void LeaderboardRoles_ListALimitedNumberOfPlaces(int roles, bool valid)
    {
        ErrorsOf(FileOf() with { Leaderboard = new LeaderboardSection { RoleIds = Ids(roles) } }).Should().BeEquivalentTo(
            valid ? [] : new[] { "Leaderboard.RoleIds may name at most 25 roles." });
    }

    [Fact]
    public void ALeaderboardChannelOfZero_IsNoDiscordId()
    {
        ErrorsOf(FileOf() with { Leaderboard = new LeaderboardSection { ChannelId = 0 } })
            .Should().Equal("Leaderboard.ChannelId is 0, which is not a Discord id.");
    }

    [Fact]
    public void TheLeaderboard_TakesWhatItIsGiven()
    {
        var date = new DateOnly(2026, 12, 24);
        var leaderboard = Plan(FileOf() with
        {
            Leaderboard = new LeaderboardSection { Days = [DayOfWeek.Friday], Dates = [date], ChannelId = 9, Top = 3 }
        }).Leaderboard;

        leaderboard.Days.Should().BeEquivalentTo([DayOfWeek.Friday]);
        leaderboard.Dates.Should().BeEquivalentTo([date]);
        leaderboard.ChannelId.Should().Be(9);
        leaderboard.Top.Should().Be(3);
    }

    [Fact]
    public void TheLeaderboardsChannel_FallsBackToTheFiles()
    {
        Plan(FileOf()).Leaderboard.ChannelId.Should().Be(ChannelId);
    }

    [Fact]
    public void TheLeaderboard_DefaultsToNoExtraDates_AndTheBuiltInTop()
    {
        var leaderboard = Plan(FileOf()).Leaderboard;

        leaderboard.Dates.Should().BeEmpty();
        leaderboard.Top.Should().Be(CountryChallengeDefaults.LeaderboardTop);
    }
}

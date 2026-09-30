using FluentAssertions;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using UseCases.UseCases.CountryChallenges.Rendering;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

/// <summary>
/// The posted messages are what the server sees, so the whole rendered text is snapshot-tested with
/// Verify; the rules around it — grouping, pings, what a failure looks like — are asserted directly.
/// </summary>
public sealed class CountryChallengeMessagesTests
{
    private const ulong MentionRoleId = 222;

    private static CountryChallengePlan FridayPlan(bool thread = false) => Plan(File(
        Fixed("Argentina Friday", DayOfWeek.Friday, Country("Argentina", "AR")) with
        {
            Settings = new GameSettingsSection { ForbidMoving = true, ForbidRotating = true, ForbidZooming = true, TimeLimit = 60 }
        },
        Pool("Small Country Friday", DayOfWeek.Friday, Country("Malta", "MT"), Country("Andorra", "AD")) with
        {
            Entry = "### {{flag}} {{name}}\nThis week: **{{country}}** ([{{mapName}}]({{mapLink}}))\n{{link}}"
        }) with
    {
        Announcement = new AnnouncementSection
        {
            MentionRoleIds = [MentionRoleId],
            Thread = new ThreadSection { Enabled = thread }
        }
    });

    private static List<AnnouncementItem> Items(CountryChallengePlan plan, params string?[] links) =>
        plan.Challenges.Select((c, i) => new AnnouncementItem(c, c.Countries[0], links[i])).ToList();

    // ---- Announcement -----------------------------------------------------

    [Fact]
    public Task Announcements_CombineTheDaysChallengesIntoOneMessage()
    {
        var plan = FridayPlan();

        var announcement = CountryChallengeMessages.Announcements(plan, Friday, Items(plan,
            "https://www.geoguessr.com/challenge/abc", "https://www.geoguessr.com/challenge/def"))
            .Should().ContainSingle().Subject;

        announcement.Message.Mentions.RoleIds.Should().Equal(MentionRoleId);
        return Verify(announcement.Message.Content);
    }

    [Fact]
    public void Announcements_ListAChallengeGeoGuessrRefused_AsAWarningLine()
    {
        var plan = FridayPlan();

        var content = CountryChallengeMessages.Announcements(plan, Friday, Items(plan, "https://link", null))
            .Single().Message.Content;

        content.Should().Contain(":warning: **Small Country Friday** (Malta) could not be created today.");
    }

    [Fact]
    public void Announcements_PingNoOneAndOpenNoThread_WhenEveryChallengeFailed()
    {
        var plan = FridayPlan(thread: true);

        var announcement = CountryChallengeMessages.Announcements(plan, Friday, Items(plan, null, null)).Single();

        announcement.Message.Mentions.IsEmpty.Should().BeTrue();
        announcement.ThreadName.Should().BeNull();
    }

    [Fact]
    public void Announcements_NameTheThreadAfterTheChallengesThatWereCreated()
    {
        var plan = FridayPlan(thread: true);

        var announcement = CountryChallengeMessages.Announcements(plan, Friday, Items(plan, "https://a", "https://b")).Single();

        announcement.ThreadName.Should().Be("Argentina Friday & Small Country Friday · 2026-10-02");
    }

    [Fact]
    public void Announcements_NameAChallengeWithSeveralPicksOnce()
    {
        var plan = Plan(Pool("Middleweight Saturday", DayOfWeek.Saturday, Country("Peru", "PE"), Country("Chile", "CL")) with
        {
            Picks = 2
        }) with
        {
            Announcement = new AnnouncementPlan("# {{names}}\n{{challenges}}", new ThreadPlan(true, "{{names}}", ThreadAutoArchive.OneDay))
        };
        var challenge = plan.Challenges.Single();

        var announcement = CountryChallengeMessages.Announcements(plan, Friday.AddDays(1),
        [
            new AnnouncementItem(challenge, challenge.Countries[0], "https://a"),
            new AnnouncementItem(challenge, challenge.Countries[1], "https://b")
        ]).Single();

        announcement.Message.Content.Should().StartWith("# Middleweight Saturday\n");
        announcement.ThreadName.Should().Be("Middleweight Saturday");
        announcement.Message.Content.Should().Contain("**Peru**").And.Contain("**Chile**");
    }

    [Fact]
    public void Announcements_PostOneMessagePerChannel()
    {
        var plan = Plan(
            Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")),
            Fixed("Chile Monday", DayOfWeek.Monday, Country("Chile")) with { ChannelId = 9 });

        var announcements = CountryChallengeMessages.Announcements(plan, Monday, Items(plan, "https://a", "https://b"));

        announcements.Select(a => (a.ChannelId, a.Items.Single().Challenge.Name))
            .Should().BeEquivalentTo([(ChannelId, "Mongolia Monday"), (9UL, "Chile Monday")]);
    }

    // ---- Results ----------------------------------------------------------

    [Fact]
    public Task Results_ListEveryEvaluatedChallengeWithItsRankingAndPoints()
    {
        var plan = FridayPlan();
        var results = plan.Challenges[0].Results;

        var messages = CountryChallengeMessages.Results(plan, Friday.AddDays(1),
        [
            new EvaluatedChallenge(
                Post("Argentina Friday", Friday, "Argentina", challengeId: "abc", countryCode: "AR"),
                results,
                [Player("a", "Anna"), Player("b", "b_o*b"), Player("c", "Cleo"), Player("d", "Dora")],
                CountryChallengeScoring.PointsByPlace(4, results.Points)),
            new EvaluatedChallenge(Post("Small Country Friday", Friday, "Malta", challengeId: "def", countryCode: "MT"), results, [], [])
        ]);

        return Verify(messages.Should().ContainSingle().Subject.Message.Content);
    }

    [Fact]
    public void Results_LeaveOutChallengesWhoseResultsAreNotPosted_AndHonourTheResultsChannel()
    {
        var plan = FridayPlan();
        var posted = plan.Challenges[0].Results with { ChannelId = 77 };
        var silent = plan.Challenges[0].Results with { Post = false };

        var messages = CountryChallengeMessages.Results(plan, Friday,
        [
            new EvaluatedChallenge(Post("Argentina Friday", Friday), posted, [Player("a")], [3]),
            new EvaluatedChallenge(Post("Small Country Friday", Friday), silent, [Player("b")], [3])
        ]);

        messages.Should().ContainSingle().Which.ChannelId.Should().Be(77UL);
        messages[0].Challenges.Should().ContainSingle();
    }

    [Fact]
    public void Results_NeverLetANicknamePingAnyone()
    {
        var plan = FridayPlan();

        var message = CountryChallengeMessages.Results(plan, Friday,
        [
            new EvaluatedChallenge(Post("Argentina Friday", Friday), plan.Challenges[0].Results, [Player("a", "<@&1> @everyone")], [3])
        ]).Single().Message;

        message.Mentions.IsEmpty.Should().BeTrue();
        message.Content.Should().Contain(@"\<@&1>");
    }

    // ---- Leaderboard ------------------------------------------------------

    [Fact]
    public Task Leaderboard_ShowsTheTopPlaces_WithTiesSharingARank()
    {
        var plan = FridayPlan();

        var message = CountryChallengeMessages.Leaderboard(plan, Monday,
        [
            new CountryChallengeStanding(1, "a", "Anna", 13),
            new CountryChallengeStanding(2, "b", "Bert", 7),
            new CountryChallengeStanding(2, "c", "Cleo", 7),
            new CountryChallengeStanding(4, "d", "Dora", 1)
        ]);

        return Verify(message.Content);
    }

    [Fact]
    public void Leaderboard_CutsAtTheConfiguredTop_ButKeepsEveryoneTiedOnTheLastPlace()
    {
        var plan = FridayPlan();
        plan = plan with { Leaderboard = plan.Leaderboard with { Top = 2 } };

        var content = CountryChallengeMessages.Leaderboard(plan, Monday,
        [
            new CountryChallengeStanding(1, "a", "Anna", 13),
            new CountryChallengeStanding(2, "b", "Bert", 7),
            new CountryChallengeStanding(2, "c", "Cleo", 7),
            new CountryChallengeStanding(4, "d", "Dora", 1)
        ]).Content;

        content.Should().Contain("Cleo").And.NotContain("Dora");
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B" }, "A & B")]
    [InlineData(new[] { "A", "B", "C" }, "A, B & C")]
    public void JoinNames_ReadsLikeAList(string[] names, string expected)
    {
        CountryChallengeMessages.JoinNames(names).Should().Be(expected);
    }
}

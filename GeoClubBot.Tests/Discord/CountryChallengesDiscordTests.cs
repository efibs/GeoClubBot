using Discord;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.CountryChallenges;
using GeoClubBot.Discord.OutputAdapters;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.CountryChallenges;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

/// <summary>The Discord side of the country challenges: who a message may ping, and the command replies.</summary>
public sealed class CountryChallengesDiscordTests
{
    // ---- Allowed mentions -------------------------------------------------

    [Fact]
    public void ToAllowedMentions_PingsOnlyTheListedRolesAndUsers()
    {
        var allowed = DiscordDiscordMessageAccess.ToAllowedMentions(new MessageMentions([1, 2], [3], Everyone: false));

        allowed.AllowedTypes.Should().Be(AllowedMentionTypes.None);
        allowed.RoleIds.Should().Equal(1UL, 2UL);
        allowed.UserIds.Should().Equal(3UL);
    }

    [Fact]
    public void ToAllowedMentions_AllowsEveryoneOnlyWhenAsked()
    {
        DiscordDiscordMessageAccess.ToAllowedMentions(new MessageMentions([], [], Everyone: true))
            .AllowedTypes.Should().Be(AllowedMentionTypes.Everyone);
    }

    [Fact]
    public void ToAllowedMentions_PingsNoOne_ForNone()
    {
        var allowed = DiscordDiscordMessageAccess.ToAllowedMentions(MessageMentions.None);

        allowed.AllowedTypes.Should().Be(AllowedMentionTypes.None);
        allowed.RoleIds.Should().BeEmpty();
        allowed.UserIds.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ThreadAutoArchive.OneHour, ThreadArchiveDuration.OneHour)]
    [InlineData(ThreadAutoArchive.OneDay, ThreadArchiveDuration.OneDay)]
    [InlineData(ThreadAutoArchive.ThreeDays, ThreadArchiveDuration.ThreeDays)]
    [InlineData(ThreadAutoArchive.OneWeek, ThreadArchiveDuration.OneWeek)]
    public void ToArchiveDuration_MapsEveryDuration(ThreadAutoArchive autoArchive, ThreadArchiveDuration expected)
    {
        DiscordDiscordMessageAccess.ToArchiveDuration(autoArchive).Should().Be(expected);
    }

    [Fact]
    public void ThreadTypeFor_AnnouncementChannel_IsANewsThread()
    {
        // Discord.Net throws "type must be a NewsThread in News channels" for anything else (#363)
        DiscordDiscordMessageAccess.ThreadTypeFor(Substitute.For<INewsChannel>())
            .Should().Be(ThreadType.NewsThread);
    }

    [Fact]
    public void ThreadTypeFor_TextChannel_IsAPublicThread()
    {
        DiscordDiscordMessageAccess.ThreadTypeFor(Substitute.For<ITextChannel>())
            .Should().Be(ThreadType.PublicThread);
    }

    // ---- Parsing the preview day ------------------------------------------

    [Theory]
    [InlineData("sunday", DayOfWeek.Sunday)]
    [InlineData("Sun", DayOfWeek.Sunday)]
    [InlineData(" tu ", DayOfWeek.Tuesday)]
    [InlineData("FRIDAY", DayOfWeek.Friday)]
    public void TryParseDay_ReadsAWeekdayWholeOrUnambiguouslyShortened(string input, DayOfWeek expected)
    {
        CountryChallengesFormatter.TryParseDay(input, out var date, out var weekday).Should().BeTrue();

        date.Should().BeNull();
        weekday.Should().Be(expected);
    }

    [Fact]
    public void TryParseDay_ReadsAnIsoDate()
    {
        CountryChallengesFormatter.TryParseDay("2026-12-24", out var date, out var weekday).Should().BeTrue();

        date.Should().Be(new DateOnly(2026, 12, 24));
        weekday.Should().BeNull();
    }

    [Theory]
    [InlineData("s")]
    [InlineData("t")]
    [InlineData("1")]
    [InlineData("24.12.2026")]
    [InlineData("someday")]
    public void TryParseDay_RefusesAnythingAmbiguousOrUnknown(string input)
    {
        CountryChallengesFormatter.TryParseDay(input, out _, out _).Should().BeFalse();
    }

    // ---- Replies ----------------------------------------------------------

    [Fact]
    public Task RunReport_SaysWhatEachPhaseDid()
    {
        var report = new CountryChallengeRunReport(
            new DateOnly(2026, 10, 2),
            ["Challenges[4] \"Japan Wednesday\": MapId is required. (the challenge is disabled, so this is only a warning)"],
            new CountryChallengeEvaluationOutcome(["Indonesia Friday (2026-09-25)"], ["Argentina Friday (2026-09-25)"]),
            new CountryChallengeLeaderboardOutcome(CountryChallengeLeaderboardStatus.NotDue),
            new CountryChallengeAnnouncementOutcome(["Indonesia Friday"], ["Argentina Friday"], []));

        return Verify(string.Join("\n---\n", CountryChallengesFormatter.RunReport(report)));
    }

    [Fact]
    public void RunReport_NamesAPhaseThatFailedOutright()
    {
        var report = new CountryChallengeRunReport(new DateOnly(2026, 10, 2), [], null, null, null);

        var text = string.Join("\n", CountryChallengesFormatter.RunReport(report));

        text.Should().Contain("Evaluating the results failed")
            .And.Contain("Posting the leaderboard failed")
            .And.Contain("Announcing today's challenges failed");
    }

    [Fact]
    public void EvaluationReport_SaysWhatWasEvaluatedAndWhatStaysPending()
    {
        var text = CountryChallengesFormatter.EvaluationReport(
            new CountryChallengeEvaluationOutcome(["Mongolia Monday (2026-09-28)"], ["Daily Wildcard (2026-09-28)"])).Single();

        text.Should().Contain("Results evaluated: Mongolia Monday (2026-09-28).")
            .And.Contain("Daily Wildcard (2026-09-28) could not be read; they stay pending.");
    }

    [Fact]
    public void EvaluationReport_SaysWhenNothingIsWaiting()
    {
        CountryChallengesFormatter.EvaluationReport(CountryChallengeEvaluationOutcome.Nothing)
            .Should().Equal("No country challenge is waiting for its results.");
    }

    [Fact]
    public void Leaderboard_HighlightsTheViewer_AndTellsThemTheirPlace()
    {
        var bert = new CountryChallengeStanding(2, "b", "Bert", 7);
        var leaderboard = new CountryChallengeLeaderboard(
            "Season 1", [new CountryChallengeStanding(1, "a", "Anna", 9), bert], bert, ViewerLinked: true);

        var text = CountryChallengesFormatter.Leaderboard(leaderboard).Single();

        text.Should().Contain("**:second_place: Bert · 7 pts**").And.Contain("You: #2 with 7 points.");
    }

    [Theory]
    [InlineData(true, "You have no points yet.")]
    [InlineData(false, "Link your GeoGuessr account with `/gg-account link` to see your own place.")]
    public void Leaderboard_ExplainsAMissingPlace(bool linked, string expected)
    {
        var leaderboard = new CountryChallengeLeaderboard("Season 1", [], null, linked);

        CountryChallengesFormatter.Leaderboard(leaderboard).Single().Should().Contain("Nobody has points yet.").And.Contain(expected);
    }

    [Fact]
    public void Preview_PutsEachMessageInItsOwnReply_UnderItsChannel()
    {
        var preview = new CountryChallengePreview(
            new DateOnly(2026, 10, 4),
            FeatureEnabled: false,
            [],
            ["Small Country Sunday picks at random from Malta, Andorra (2 of 2 left in this round); previewed with Malta."],
            [new CountryChallengePreviewMessage("Announcement", 42, "# Sunday\nhttps://www.geoguessr.com/challenge/PREVIEW")]);

        var replies = CountryChallengesFormatter.Preview(preview);

        replies.Should().HaveCount(2);
        replies[0].Should().StartWith("## Preview of Sunday, 2026-10-04")
            .And.Contain("switched off")
            .And.Contain("- Small Country Sunday picks at random");
        replies[1].Should().Be("**Announcement** → <#42>\n# Sunday\nhttps://www.geoguessr.com/challenge/PREVIEW");
    }

    [Fact]
    public void Split_KeepsEveryReplyWithinDiscordsLimit()
    {
        var longText = string.Join("\n", Enumerable.Repeat(new string('x', 99), 60));

        CountryChallengesFormatter.Split([longText]).Should().HaveCountGreaterThan(1)
            .And.OnlyContain(part => part.Length <= 2000);
    }
}

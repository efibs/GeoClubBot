using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using UseCases.UseCases.CountryChallenges.Rendering;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

/// <summary>The read-only side: the admin preview and the public leaderboard.</summary>
public sealed class CountryChallengeQueriesTests
{
    private static readonly Guid MainClubId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset AwardedAt = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly ICountryChallengeRepository _repository = Substitute.For<ICountryChallengeRepository>();
    private readonly IGeoGuessrClientFactory _factory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();

    private CountryChallengePlan _plan = Plan(File(
        Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")),
        Pool("Small Country Sunday", DayOfWeek.Sunday, Country("Malta"), Country("Andorra"), Country("Monaco"))) with
    {
        Announcement = new AnnouncementSection { MentionRoleIds = [222] }
    });

    public CountryChallengeQueriesTests()
    {
        _mediator.Send(Arg.Any<LoadCountryChallengePlanQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result<CountryChallengePlan>.Success(_plan));
        _factory.CreateClient(MainClubId).Returns(_client);
        _repository.ReadPostsOnAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _repository.ReadCountryHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _repository.ReadPostsDueForEvaluationAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _repository.ReadAwardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
        [
            CountryChallengePointAward.Imported("Season 1", "anna-id", "Anna", 9, AwardedAt),
            CountryChallengePointAward.Imported("Season 1", "bert-id", "Bert", 4, AwardedAt)
        ]);
    }

    // ---- Preview ----------------------------------------------------------

    [Fact]
    public async Task Preview_ShowsTheAnnouncementWithAPlaceholderLink_AndCreatesNothing()
    {
        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Date.Should().Be(Monday);
        var message = preview.Messages.Single(m => m.Title == "Announcement");
        message.ChannelId.Should().Be(ChannelId);
        message.Content.Should().Contain("Mongolia Monday").And.Contain(CountryChallengeMessages.PreviewLink);
        preview.Notes.Should().Contain("The announcement pings <@&222>.");
        await _client.DidNotReceiveWithAnyArgs().CreateChallengeAsync(default!, default);
    }

    [Fact]
    public async Task Preview_ListsWhichPoolCountriesAreLeftInTheRound()
    {
        _repository.ReadCountryHistoryAsync("Small Country Sunday", Arg.Any<CancellationToken>()).Returns(["Malta"]);

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Sunday))).Value;

        preview.Notes.Should().Contain(n => n.StartsWith("Small Country Sunday picks at random from Andorra, Monaco (2 of 3 left in this round)"));
    }

    [Fact]
    public async Task Preview_IncludesTheLeaderboard_CountingTheResultsDueTheSameDay()
    {
        var due = Post("Mongolia Monday", Monday.AddDays(-7), challengeId: "old");
        _repository.ReadPostsDueForEvaluationAsync(Monday, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([due]);
        _client.ReadHighscoresAsync("old", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores("bert-id", "cleo-id"));

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Messages.Select(m => m.Title).Should().Equal("Results", "Leaderboard (Season 1)", "Announcement");
        // Bert had 4 and wins the due challenge (+3): 7. Cleo gets 2 from it.
        preview.Messages[1].Content.Should().Contain(":first_place: Anna · 9 pts")
            .And.Contain(":second_place: nick-bert-id · 7 pts")
            .And.Contain(":third_place: nick-cleo-id · 2 pts");
    }

    [Fact]
    public async Task Preview_TurnsAWeekdayIntoItsNextOccurrence()
    {
        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Weekday: DayOfWeek.Sunday))).Value;

        preview.Date.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        preview.Date.Should().BeOnOrAfter(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        preview.FeatureEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_SaysWhenNothingIsScheduled()
    {
        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday.AddDays(1)))).Value;

        preview.Messages.Should().BeEmpty();
        preview.Notes.Should().Contain("No challenge is scheduled for Tuesday 2026-09-29.");
    }

    [Fact]
    public async Task Preview_PassesOnAPlanThatCannotBeLoaded()
    {
        var error = Error.Validation("country-challenges.invalid", "broken file");
        _mediator.Send(Arg.Any<LoadCountryChallengePlanQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<CountryChallengePlan>.Failure(error));

        var result = await PreviewAsync(new PreviewCountryChallengesQuery(Monday));

        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task Preview_DefaultsToToday()
    {
        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery())).Value;

        // The feature's time zone may be a day off UTC.
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        preview.Date.Should().BeOnOrAfter(utcToday.AddDays(-1)).And.BeOnOrBefore(utcToday.AddDays(1));
    }

    [Fact]
    public async Task Preview_ReportsWhetherTheFeatureIsOn()
    {
        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday), enabled: true)).Value;

        preview.FeatureEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Preview_SaysWhenAChallengeCloses_BecauseItsResultsWereSwitchedOff()
    {
        _plan = Plan(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")) with
        {
            Results = new ChallengeResultsSection { Enabled = false }
        });
        ResultsAreDue();

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain($"{DueLabel}: results are switched off, so it is closed without results.");
        preview.Notes.Should().NotContain(n => n.StartsWith("Evaluates"));
        preview.Messages.Should().NotContain(m => m.Title == "Results");
        await _client.DidNotReceiveWithAnyArgs().ReadHighscoresAsync(default!, default!, default);
    }

    [Fact]
    public async Task Preview_SaysWhenHighscoresCannotBeRead_AndLeavesThatChallengeOut()
    {
        ResultsAreDue();
        _client.ReadHighscoresAsync("old", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain($"{DueLabel}: its highscores could not be read right now (boom).");
        preview.Notes.Should().NotContain(n => n.StartsWith("Evaluates"));
        preview.Messages.Should().NotContain(m => m.Title == "Results");
    }

    [Fact]
    public async Task Preview_ReadsHighscoresWithTheSameRulesAsTheEvaluation()
    {
        ResultsAreDue("bert-id");

        await PreviewAsync(new PreviewCountryChallengesQuery(Monday));

        await _client.Received(1).ReadHighscoresAsync(
            "old",
            Arg.Is<ReadHighscoresQueryParams>(q => q.MinRounds == 5 && q.Limit > 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Preview_DescribesTheEvaluation_WithoutRolesUnlessThereAreAny()
    {
        ResultsAreDue("bert-id");

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain($"Evaluates {DueLabel} with the highscores as they are now.");
        preview.Notes.Should().NotContain(n => n.Contains("not posted"));
        preview.Messages.Should().Contain(m => m.Title == "Results");
    }

    [Fact]
    public async Task Preview_MentionsTheRoles_AndThatUnpostedResultsStillCount()
    {
        _plan = Plan(File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with
        {
            Results = new ResultsSection { RoleIds = [1, 2], Post = false }
        });
        ResultsAreDue("bert-id");

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain($"Evaluates {DueLabel} with the highscores as they are now, and hands out 2 role(s).");
        preview.Notes.Should().Contain($"{DueLabel}: its results are not posted (Results.Post is false); the points still count.");
        preview.Messages.Should().NotContain(m => m.Title == "Results");
    }

    [Fact]
    public async Task Preview_LeavesTheLeaderboardOut_OnADayItIsNotDue()
    {
        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Sunday))).Value;

        preview.Messages.Should().NotContain(m => m.Title.StartsWith("Leaderboard"));
        await _repository.DidNotReceiveWithAnyArgs().IsLeaderboardPostedOnAsync(default, default);
    }

    [Fact]
    public async Task Preview_SaysWhenTheLeaderboardWasAlreadyPosted()
    {
        _repository.IsLeaderboardPostedOnAsync(Monday, Arg.Any<CancellationToken>()).Returns(true);

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain("The leaderboard was already posted on this day.");
        preview.Messages.Should().NotContain(m => m.Title.StartsWith("Leaderboard"));
    }

    [Fact]
    public async Task Preview_SaysWhenNobodyHasPointsForTheLeaderboardYet()
    {
        _repository.ReadAwardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain("The leaderboard is due, but nobody has points yet, so it is not posted.");
        preview.Messages.Should().NotContain(m => m.Title.StartsWith("Leaderboard"));
    }

    [Fact]
    public async Task Preview_SaysWhenAChallengeWasAlreadyPostedThatDay_WhateverItsCase()
    {
        _repository.ReadPostsOnAsync(Monday, Arg.Any<CancellationToken>()).Returns([Post("MONGOLIA MONDAY", Monday)]);

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain("Mongolia Monday was already posted on this day.");
        preview.Messages.Should().NotContain(m => m.Title == "Announcement");
    }

    [Fact]
    public async Task Preview_SaysHowManyCountriesAreDrawn_AndWhichOnes()
    {
        _plan = Plan(Pool("Small Country Sunday", DayOfWeek.Sunday, Country("Malta"), Country("Andorra"), Country("Monaco")) with { Picks = 2 });

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Sunday))).Value;

        preview.Notes.Should().ContainMatch(
            "Small Country Sunday picks 2 at random from Malta, Andorra, Monaco (3 of 3 left in this round); previewed with * and *.");
    }

    [Fact]
    public async Task Preview_OnlyDrawsThePicksStillMissing_AndNeverACountryPlayedThatDay()
    {
        _plan = Plan(Pool("Small Country Sunday", DayOfWeek.Sunday, Country("Malta"), Country("Andorra"), Country("Monaco")) with { Picks = 2 });
        _repository.ReadPostsOnAsync(Sunday, Arg.Any<CancellationToken>()).Returns([Post("Small Country Sunday", Sunday, country: "Malta")]);

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Sunday))).Value;

        preview.Notes.Should().ContainMatch("Small Country Sunday picks at random from *");
        preview.Messages.Single(m => m.Title == "Announcement").Content.Should().NotContain("Malta");
    }

    [Fact]
    public async Task Preview_HasNoRoundToExplain_WhenThereIsNoChoice()
    {
        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().NotContain(n => n.Contains("at random"));
    }

    [Fact]
    public async Task Preview_SaysWhenTheAnnouncementPingsNoOne()
    {
        _plan = Plan(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")));

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain("The announcement pings no one.");
    }

    [Fact]
    public async Task Preview_ListsEveryKindOfPing()
    {
        _plan = Plan(File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with
        {
            Announcement = new AnnouncementSection { Message = "@everyone <@12> {{challenges}}", MentionRoleIds = [222] }
        });

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain("The announcement pings <@&222>, <@12>, @everyone/@here.");
    }

    [Fact]
    public async Task Preview_NamesTheThreadItWouldOpen_OnlyWhenThereIsOne()
    {
        var withoutThread = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;
        _plan = Plan(File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with
        {
            Announcement = new AnnouncementSection
            {
                Thread = new ThreadSection { Enabled = true, Name = "Today", AutoArchive = ThreadAutoArchive.ThreeDays }
            }
        });

        var withThread = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        withoutThread.Notes.Should().NotContain(n => n.StartsWith("Opens the thread"));
        withThread.Notes.Should().Contain("Opens the thread \"Today\" (ThreeDays).");
    }

    private CountryChallengePlan PlanWithThreadName(string name) =>
        Plan(File(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia"))) with
        {
            Announcement = new AnnouncementSection { Thread = new ThreadSection { Enabled = true, Name = name } }
        });

    [Fact]
    public async Task Preview_KeepsAThreadNameOfExactlyDiscordsLimit()
    {
        var name = new string('x', 100);
        _plan = PlanWithThreadName(name);

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain($"Opens the thread \"{name}\" (OneDay).");
    }

    [Fact]
    public async Task Preview_CutsALongerThreadName_ToDiscordsLimit()
    {
        _plan = PlanWithThreadName(new string('x', 101));

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Notes.Should().Contain($"Opens the thread \"{new string('x', 99)}…\" (OneDay).");
    }

    [Fact]
    public async Task Preview_RendersEveryEntryPlaceholder()
    {
        _plan = Plan(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")) with
        {
            Entry = "{{mode}}|{{timeLimit}}|{{mapName}}|{{mentions}}",
            MentionRoleIds = [5, 6]
        });
        var settings = CountryChallengeDefaults.Settings;

        var preview = (await PreviewAsync(new PreviewCountryChallengesQuery(Monday))).Value;

        preview.Messages.Single(m => m.Title == "Announcement").Content.Should().Contain(
            $"{ChallengeSettingsText.Mode(settings)}|{ChallengeSettingsText.TimeLimit(settings.TimeLimit)}|Mongolia|<@&5> <@&6>");
    }

    // ---- Leaderboard ------------------------------------------------------

    [Fact]
    public async Task Leaderboard_ShowsTheStandingsAndTheViewersOwnPlace()
    {
        _users.ReadUserByDiscordUserIdAsync(55, Arg.Any<CancellationToken>()).Returns(GeoGuessrUser.Create("bert-id", "Bert", 55));

        var leaderboard = (await LeaderboardAsync(enabled: true, viewer: 55)).Value;

        leaderboard.Season.Should().Be("Season 1");
        leaderboard.Top.Select(s => s.Nickname).Should().Equal("Anna", "Bert");
        leaderboard.Viewer!.Rank.Should().Be(2);
        leaderboard.ViewerLinked.Should().BeTrue();
    }

    [Fact]
    public async Task Leaderboard_KnowsWhenTheViewerHasNoLinkedAccount()
    {
        var leaderboard = (await LeaderboardAsync(enabled: true, viewer: 99)).Value;

        leaderboard.Viewer.Should().BeNull();
        leaderboard.ViewerLinked.Should().BeFalse();
    }

    [Fact]
    public async Task Leaderboard_IsUnavailable_WhileTheFeatureIsSwitchedOff()
    {
        var result = await LeaderboardAsync(enabled: false, viewer: null);

        result.Error.Code.Should().Be(RunCountryChallengesHandler.DisabledCode);
    }

    private const string DueLabel = "Mongolia Monday of 2026-09-21";

    /// <summary>Makes last Monday's challenge due on <see cref="Monday"/>, with these highscores, best first.</summary>
    private void ResultsAreDue(params string[] userIds)
    {
        _repository.ReadPostsDueForEvaluationAsync(
                Monday, Monday.AddDays(-EvaluateCountryChallengesHandler.GraceDays), Arg.Any<CancellationToken>())
            .Returns([Post("Mongolia Monday", Monday.AddDays(-7), challengeId: "old")]);
        _client.ReadHighscoresAsync("old", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores(userIds));
    }

    private Task<Result<CountryChallengePreview>> PreviewAsync(PreviewCountryChallengesQuery query, bool enabled = false) =>
        new PreviewCountryChallengesHandler(
                _mediator,
                _repository,
                _factory,
                new GeoGuessrConfigurationBuilder().WithClub(MainClubId).BuildOptions(),
                Options.Create(new CountryChallengesConfiguration { Enabled = enabled, Schedule = "0 0 17 ? * * *" }))
            .Handle(query, CancellationToken.None);

    private Task<Result<CountryChallengeLeaderboard>> LeaderboardAsync(bool enabled, ulong? viewer) =>
        new GetCountryChallengeLeaderboardHandler(
                _mediator,
                _repository,
                _users,
                Options.Create(new CountryChallengesConfiguration { Enabled = enabled, Schedule = "0 0 17 ? * * *" }))
            .Handle(new GetCountryChallengeLeaderboardQuery(viewer), CancellationToken.None);
}

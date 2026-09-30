using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Options;
using NSubstitute;
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

    private readonly CountryChallengePlan _plan = Plan(File(
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

    private Task<Result<CountryChallengePreview>> PreviewAsync(PreviewCountryChallengesQuery query) =>
        new PreviewCountryChallengesHandler(
                _mediator,
                _repository,
                _factory,
                new GeoGuessrConfigurationBuilder().WithClub(MainClubId).BuildOptions(),
                Options.Create(new CountryChallengesConfiguration { Schedule = "0 0 17 ? * * *" }))
            .Handle(query, CancellationToken.None);

    private Task<Result<CountryChallengeLeaderboard>> LeaderboardAsync(bool enabled, ulong? viewer) =>
        new GetCountryChallengeLeaderboardHandler(
                _mediator,
                _repository,
                _users,
                Options.Create(new CountryChallengesConfiguration { Enabled = enabled, Schedule = "0 0 17 ? * * *" }))
            .Handle(new GetCountryChallengeLeaderboardQuery(viewer), CancellationToken.None);
}

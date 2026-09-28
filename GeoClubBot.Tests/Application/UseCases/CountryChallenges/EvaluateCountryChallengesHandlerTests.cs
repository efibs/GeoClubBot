using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

/// <summary>
/// Evaluating a challenge awards points that count forever, so it must happen exactly once: after the
/// points are stored, and never for a challenge whose highscores could not be read.
/// </summary>
public sealed class EvaluateCountryChallengesHandlerTests
{
    private static readonly Guid MainClubId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly Saturday = Friday.AddDays(1);

    private readonly IGeoGuessrClientFactory _factory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly ICountryChallengeRepository _repository = Substitute.For<ICountryChallengeRepository>();
    private readonly IDiscordMessageAccess _discord = Substitute.For<IDiscordMessageAccess>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly List<CountryChallengePost> _due = [];
    private readonly List<CountryChallengePointAward> _awards = [];
    private readonly List<string> _posted = [];

    public EvaluateCountryChallengesHandlerTests()
    {
        _factory.CreateClient(MainClubId).Returns(_client);
        _repository.ReadPostsDueForEvaluationAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_ => [.. _due]);
        _client.ReadHighscoresAsync(Arg.Any<string>(), Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores());
        _repository.When(r => r.AddAwards(Arg.Any<IEnumerable<CountryChallengePointAward>>()))
            .Do(call => _awards.AddRange(call.Arg<IEnumerable<CountryChallengePointAward>>()));
        _discord.SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<MessageMentions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _posted.Add(call.ArgAt<string>(0));
                return 1UL;
            });
    }

    private static CountryChallengePlan FridayPlan(List<ulong>? roleIds = null, bool post = true) => Plan(File(
        Fixed("Argentina Friday", DayOfWeek.Friday, Country("Argentina")),
        Fixed("Indonesia Friday", DayOfWeek.Friday, Country("Indonesia")) with
        {
            Results = new ChallengeResultsSection { Points = [5, 3] }
        }) with
    {
        Results = new ResultsSection { RoleIds = roleIds ?? [], Post = post },
        Leaderboard = new LeaderboardSection { Season = "Season 2" }
    });

    [Fact]
    public async Task Handle_AwardsThePointsOfEachPlace_AndMarksTheChallengeEvaluated()
    {
        var argentina = ArrangeDue(Post("Argentina Friday", Friday, "Argentina", challengeId: "arg"), "anna", "bert", "cleo", "dora");

        var outcome = await HandleAsync(FridayPlan());

        outcome.Evaluated.Should().Equal("Argentina Friday (2026-10-02)");
        _awards.Select(a => (a.Season, a.UserId, a.Points, a.Place))
            .Should().Equal(("Season 2", "anna", 3, 1), ("Season 2", "bert", 2, 2), ("Season 2", "cleo", 1, 3));
        argentina.EvaluatedAt.Should().NotBeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReadsEnoughHighscoresForTheListAndThePoints()
    {
        ArrangeDue(Post("Indonesia Friday", Friday, "Indonesia", challengeId: "ind"));

        await HandleAsync(FridayPlan());

        await _client.Received(1).ReadHighscoresAsync(
            "ind", Arg.Is<ReadHighscoresQueryParams>(p => p.Limit == 10 && p.MinRounds == 5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UsesEachChallengesOwnPoints_AndPostsOneCombinedResultsMessage()
    {
        ArrangeDue(Post("Argentina Friday", Friday, "Argentina", challengeId: "arg"), "anna");
        ArrangeDue(Post("Indonesia Friday", Friday, "Indonesia", challengeId: "ind"), "bert");

        await HandleAsync(FridayPlan());

        _awards.Select(a => (a.UserId, a.Points)).Should().Equal(("anna", 3), ("bert", 5));
        _posted.Should().ContainSingle().Which.Should().Contain("Argentina Friday").And.Contain("Indonesia Friday");
    }

    [Fact]
    public async Task Handle_StillCountsThePoints_WhenTheResultsAreNotPosted()
    {
        ArrangeDue(Post("Argentina Friday", Friday), "anna");

        await HandleAsync(FridayPlan(post: false));

        _awards.Should().ContainSingle();
        _posted.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_HandsOutTheRoles_ToTheBestOfAllChallengesSharingThem()
    {
        ArrangeDue(Post("Argentina Friday", Friday, challengeId: "arg"), "anna", "bert");
        ArrangeDue(Post("Indonesia Friday", Friday, challengeId: "ind"), "bert", "cleo");

        await HandleAsync(FridayPlan(roleIds: [1, 2]));

        await _mediator.Received(1).Send(
            Arg.Is<DistributeCountryChallengeRolesCommand>(c =>
                c.Assignment.RoleIdsByPlace.SequenceEqual(new ulong[] { 1, 2 })
                && c.Assignment.PlayersByPlace[0].OrderBy(p => p).SequenceEqual(new[] { "anna", "bert" })
                && c.Assignment.PlayersByPlace[1].SequenceEqual(new[] { "cleo" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LeavesAChallengePending_WhenItsHighscoresCannotBeRead()
    {
        var argentina = ArrangeDue(Post("Argentina Friday", Friday, challengeId: "arg"), "anna");
        var indonesia = ArrangeDue(Post("Indonesia Friday", Friday, challengeId: "ind"), "bert");
        _client.ReadHighscoresAsync("arg", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("GeoGuessr is down"));

        var outcome = await HandleAsync(FridayPlan());

        outcome.Failed.Should().Equal("Argentina Friday (2026-10-02)");
        outcome.Evaluated.Should().Equal("Indonesia Friday (2026-10-02)");
        argentina.EvaluatedAt.Should().BeNull("the next run tries again");
        indonesia.EvaluatedAt.Should().NotBeNull();
        _awards.Should().OnlyContain(a => a.UserId == "bert");
    }

    [Fact]
    public async Task Handle_ClosesAChallengeWithoutResults_WhenResultsWereSwitchedOffSincePosting()
    {
        var argentina = ArrangeDue(Post("Argentina Friday", Friday), "anna");
        var plan = Plan(File(Fixed("Argentina Friday", DayOfWeek.Friday, Country("Argentina")) with
        {
            Results = new ChallengeResultsSection { Enabled = false }
        }));

        var outcome = await HandleAsync(plan);

        argentina.EvaluatedAt.Should().NotBeNull();
        outcome.Evaluated.Should().BeEmpty();
        await _client.DidNotReceiveWithAnyArgs().ReadHighscoresAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_PostsNothing_WhenTheEvaluationCannotBeStored()
    {
        ArrangeDue(Post("Argentina Friday", Friday), "anna");
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database down"));

        var outcome = await HandleAsync(FridayPlan(roleIds: [1]));

        outcome.Evaluated.Should().BeEmpty();
        outcome.Failed.Should().ContainSingle();
        _posted.Should().BeEmpty("announced points must also have been counted");
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<DistributeCountryChallengeRolesCommand>(), default);
    }

    [Fact]
    public async Task Handle_StillHandsOutTheRoles_WhenTheResultsCannotBePosted()
    {
        ArrangeDue(Post("Argentina Friday", Friday), "anna");
        _discord.SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<MessageMentions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Discord is down"));

        await HandleAsync(FridayPlan(roleIds: [1]));

        await _mediator.Received(1).Send(Arg.Any<DistributeCountryChallengeRolesCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EvaluatesEveryPendingChallenge_WhenAskedNotToWaitForItsDay()
    {
        var early = Post("Argentina Friday", Saturday, challengeId: "early", resultsDueOn: Saturday.AddDays(7));
        _client.ReadHighscoresAsync("early", Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores("anna"));
        _repository.ReadPendingPostsAsync(Arg.Any<CancellationToken>()).Returns([early]);

        var outcome = await new EvaluateCountryChallengesHandler(
                _factory, _repository, _discord, _mediator, _unitOfWork,
                new GeoGuessrConfigurationBuilder().WithClub(MainClubId).BuildOptions(),
                NullLogger<EvaluateCountryChallengesHandler>.Instance)
            .Handle(new EvaluateCountryChallengesCommand(FridayPlan(), Saturday, IncludeNotYetDue: true), CancellationToken.None);

        outcome.Evaluated.Should().Equal("Argentina Friday (2026-10-03)");
        early.EvaluatedAt.Should().NotBeNull();
        _awards.Should().ContainSingle().Which.UserId.Should().Be("anna");
        await _repository.DidNotReceiveWithAnyArgs().ReadPostsDueForEvaluationAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_OnlyLooksAtChallengesDueInTheLastWeek()
    {
        await HandleAsync(FridayPlan());

        await _repository.Received(1).ReadPostsDueForEvaluationAsync(
            Saturday, Saturday.AddDays(-EvaluateCountryChallengesHandler.GraceDays), Arg.Any<CancellationToken>());
        _factory.DidNotReceiveWithAnyArgs().CreateClient(default);
    }

    /// <summary>Makes <paramref name="post"/> due, with <paramref name="userIds"/> as its highscores, best first.</summary>
    private CountryChallengePost ArrangeDue(CountryChallengePost post, params string[] userIds)
    {
        _due.Add(post);
        _client.ReadHighscoresAsync(post.ChallengeId, Arg.Any<ReadHighscoresQueryParams>(), Arg.Any<CancellationToken>())
            .Returns(Highscores(userIds));
        return post;
    }

    private Task<CountryChallengeEvaluationOutcome> HandleAsync(CountryChallengePlan plan) =>
        new EvaluateCountryChallengesHandler(
                _factory,
                _repository,
                _discord,
                _mediator,
                _unitOfWork,
                new GeoGuessrConfigurationBuilder().WithClub(MainClubId).BuildOptions(),
                NullLogger<EvaluateCountryChallengesHandler>.Instance)
            .Handle(new EvaluateCountryChallengesCommand(plan, Saturday), CancellationToken.None);
}

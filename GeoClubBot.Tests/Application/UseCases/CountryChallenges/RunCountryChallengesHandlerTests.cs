using Configuration;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

/// <summary>
/// The run ties the phases together. What matters is that it respects the master switch and a broken
/// file, and that one failing phase never costs the players the others.
/// </summary>
public sealed class RunCountryChallengesHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly CountryChallengePlan _plan = Plan(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")));

    public RunCountryChallengesHandlerTests()
    {
        _mediator.Send(Arg.Any<LoadCountryChallengePlanQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<CountryChallengePlan>.Success(_plan));
        _mediator.Send(Arg.Any<EvaluateCountryChallengesCommand>(), Arg.Any<CancellationToken>())
            .Returns(new CountryChallengeEvaluationOutcome(["Old (2026-09-21)"], []));
        _mediator.Send(Arg.Any<PostCountryChallengeLeaderboardCommand>(), Arg.Any<CancellationToken>())
            .Returns(new CountryChallengeLeaderboardOutcome(CountryChallengeLeaderboardStatus.Posted));
        _mediator.Send(Arg.Any<AnnounceCountryChallengesCommand>(), Arg.Any<CancellationToken>())
            .Returns(new CountryChallengeAnnouncementOutcome(["Mongolia Monday"], [], []));
    }

    [Fact]
    public async Task Handle_RunsEveryPhaseForTheDay_AndReportsWhatEachDid()
    {
        var result = await HandleAsync(enabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new
        {
            Date = Monday,
            Evaluation = new { Evaluated = new[] { "Old (2026-09-21)" } },
            Leaderboard = new { Status = CountryChallengeLeaderboardStatus.Posted },
            Announcement = new { Announced = new[] { "Mongolia Monday" } }
        });
        await _mediator.Received(1).Send(
            Arg.Is<AnnounceCountryChallengesCommand>(c => c.Date == Monday && c.Plan == _plan), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNothing_WhileTheFeatureIsSwitchedOff()
    {
        var result = await HandleAsync(enabled: false);

        result.Error.Code.Should().Be(RunCountryChallengesHandler.DisabledCode);
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<LoadCountryChallengePlanQuery>(), default);
    }

    [Fact]
    public async Task Handle_RunsNoPhase_WhenTheFileIsBroken()
    {
        _mediator.Send(Arg.Any<LoadCountryChallengePlanQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<CountryChallengePlan>.Failure(Error.Validation("x", "• Challenges[0]: MapId is required.")));

        var result = await HandleAsync(enabled: true);

        result.Error.Message.Should().Contain("MapId is required");
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<AnnounceCountryChallengesCommand>(), default);
    }

    [Fact]
    public async Task Handle_StillAnnouncesTodaysChallenges_WhenTheEarlierPhasesFail()
    {
        _mediator.Send(Arg.Any<EvaluateCountryChallengesCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database down"));
        _mediator.Send(Arg.Any<PostCountryChallengeLeaderboardCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database down"));

        var result = await HandleAsync(enabled: true);

        result.Value.Evaluation.Should().BeNull();
        result.Value.Leaderboard.Should().BeNull();
        result.Value.Announcement!.Announced.Should().Equal("Mongolia Monday");
    }

    private Task<Result<CountryChallengeRunReport>> HandleAsync(bool enabled) =>
        new RunCountryChallengesHandler(
                _mediator,
                Options.Create(new CountryChallengesConfiguration { Enabled = enabled, Schedule = "0 0 17 ? * * *" }),
                NullLogger<RunCountryChallengesHandler>.Instance)
            .Handle(new RunCountryChallengesCommand(Monday), CancellationToken.None);
}

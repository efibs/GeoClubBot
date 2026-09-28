using Configuration;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

/// <summary><c>/country-challenges-admin results-now</c>: evaluate what is pending, without waiting for its day.</summary>
public sealed class EvaluateCountryChallengesNowHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly CountryChallengePlan _plan = Plan(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")));

    public EvaluateCountryChallengesNowHandlerTests()
    {
        _mediator.Send(Arg.Any<LoadCountryChallengePlanQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<CountryChallengePlan>.Success(_plan));
        _mediator.Send(Arg.Any<EvaluateCountryChallengesCommand>(), Arg.Any<CancellationToken>())
            .Returns(new CountryChallengeEvaluationOutcome(["Mongolia Monday (2026-09-28)"], []));
    }

    [Fact]
    public async Task Handle_EvaluatesEveryPendingChallenge_AsOfToday()
    {
        // The configured zone is UTC; a run crossing midnight may see either day.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await HandleAsync(enabled: true);

        result.Value.Evaluated.Should().Equal("Mongolia Monday (2026-09-28)");
        await _mediator.Received(1).Send(
            Arg.Is<EvaluateCountryChallengesCommand>(c =>
                c.IncludeNotYetDue && c.Plan == _plan && (c.Date == today || c.Date == today.AddDays(1))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNothing_WhileTheFeatureIsSwitchedOff()
    {
        var result = await HandleAsync(enabled: false);

        result.Error.Code.Should().Be(RunCountryChallengesHandler.DisabledCode);
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<EvaluateCountryChallengesCommand>(), default);
    }

    [Fact]
    public async Task Handle_EvaluatesNothing_WhenTheFileIsBroken()
    {
        _mediator.Send(Arg.Any<LoadCountryChallengePlanQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<CountryChallengePlan>.Failure(Error.Validation("x", "broken")));

        var result = await HandleAsync(enabled: true);

        result.Error.Message.Should().Be("broken");
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<EvaluateCountryChallengesCommand>(), default);
    }

    private Task<Result<CountryChallengeEvaluationOutcome>> HandleAsync(bool enabled) =>
        new EvaluateCountryChallengesNowHandler(
                _mediator,
                Options.Create(new CountryChallengesConfiguration { Enabled = enabled, Schedule = "0 0 17 ? * * *" }))
            .Handle(new EvaluateCountryChallengesNowCommand(), CancellationToken.None);
}

using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Strikes;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Strikes;

public sealed class CheckStrikeDecayHandlerTests
{
    private readonly IStrikesRepository _strikes = Substitute.For<IStrikesRepository>();
    private readonly ILogger<CheckStrikeDecayHandler> _logger = Substitute.For<ILogger<CheckStrikeDecayHandler>>();

    [Fact]
    public async Task Handle_DeletesStrikesOlderThanConfiguredDecaySpan()
    {
        var decay = TimeSpan.FromDays(60);
        var handler = CreateHandler(decay);
        var before = DateTimeOffset.UtcNow;

        _strikes.DeleteStrikesBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(7);

        await handler.Handle(new CheckStrikeDecayCommand(), CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        var expectedLowerBound = before - decay;
        var expectedUpperBound = after - decay;

        await _strikes.Received(1).DeleteStrikesBeforeAsync(
            Arg.Is<DateTimeOffset>(ts => ts >= expectedLowerBound && ts <= expectedUpperBound),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsUnit()
    {
        var handler = CreateHandler(TimeSpan.FromDays(30));

        var result = await handler.Handle(new CheckStrikeDecayCommand(), CancellationToken.None);

        result.Should().Be(MediatR.Unit.Value);
    }

    private CheckStrikeDecayHandler CreateHandler(TimeSpan decay)
    {
        var options = Options.Create(new ActivityCheckerConfiguration
        {
            Schedule = "0 * * * * ?",
            TextChannelId = 1UL,
            MinXP = 100,
            GracePeriodDays = 7,
            MaxNumStrikes = 3,
            HistoryKeepTimeSpan = TimeSpan.FromDays(90),
            StrikeDecayTimeSpan = decay
        });

        return new CheckStrikeDecayHandler(_strikes, _logger, options);
    }
}

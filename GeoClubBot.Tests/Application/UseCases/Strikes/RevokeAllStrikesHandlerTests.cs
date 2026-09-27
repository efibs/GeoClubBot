using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Strikes;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Strikes;

public sealed class RevokeAllStrikesHandlerTests
{
    private readonly IStrikesRepository _strikes = Substitute.For<IStrikesRepository>();

    private RevokeAllStrikesHandler CreateHandler() =>
        new(_strikes, Substitute.For<ILogger<RevokeAllStrikesHandler>>());

    [Fact]
    public async Task RevokesEveryActiveStrike_AndReturnsTheCount()
    {
        var first = ClubMemberStrike.Create("user-1", DateTimeOffset.UtcNow.AddDays(-2));
        var second = ClubMemberStrike.Create("user-2", DateTimeOffset.UtcNow.AddDays(-1));
        _strikes.ReadAllActiveForUpdateAsync(Arg.Any<CancellationToken>()).Returns([first, second]);

        var numRevoked = await CreateHandler().Handle(new RevokeAllStrikesCommand(), CancellationToken.None);

        numRevoked.Should().Be(2);
        first.Revoked.Should().BeTrue();
        second.Revoked.Should().BeTrue();
    }

    [Fact]
    public async Task ReturnsZero_WhenThereAreNoActiveStrikes()
    {
        _strikes.ReadAllActiveForUpdateAsync(Arg.Any<CancellationToken>()).Returns([]);

        var numRevoked = await CreateHandler().Handle(new RevokeAllStrikesCommand(), CancellationToken.None);

        numRevoked.Should().Be(0);
    }
}

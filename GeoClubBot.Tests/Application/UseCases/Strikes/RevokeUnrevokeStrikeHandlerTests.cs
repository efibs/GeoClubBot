using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UseCases.OutputPorts;
using UseCases.UseCases.Strikes;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Strikes;

public sealed class RevokeUnrevokeStrikeHandlerTests
{
    private readonly IStrikesRepository _strikes = Substitute.For<IStrikesRepository>();

    private RevokeStrikeHandler CreateRevokeHandler() =>
        new(_strikes, Substitute.For<ILogger<RevokeStrikeHandler>>());

    private UnrevokeStrikeHandler CreateUnrevokeHandler() =>
        new(_strikes, Substitute.For<ILogger<UnrevokeStrikeHandler>>());

    [Fact]
    public async Task Revoke_MarksStrikeRevoked_AndReturnsIt()
    {
        var strike = ClubMemberStrike.Create("user-1", DateTimeOffset.UtcNow);
        _strikes.ReadForUpdateByIdAsync(strike.StrikeId, Arg.Any<CancellationToken>()).Returns(strike);

        var result = await CreateRevokeHandler().Handle(new RevokeStrikeCommand(strike.StrikeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(strike);
        strike.Revoked.Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_ReturnsNotFound_WhenStrikeMissing()
    {
        var id = Guid.NewGuid();
        _strikes.ReadForUpdateByIdAsync(id, Arg.Any<CancellationToken>()).Returns((ClubMemberStrike?)null);

        var result = await CreateRevokeHandler().Handle(new RevokeStrikeCommand(id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("strike.not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Unrevoke_ClearsRevoked_AndReturnsIt()
    {
        var strike = ClubMemberStrike.Create("user-1", DateTimeOffset.UtcNow);
        strike.Revoke();
        _strikes.ReadForUpdateByIdAsync(strike.StrikeId, Arg.Any<CancellationToken>()).Returns(strike);

        var result = await CreateUnrevokeHandler().Handle(new UnrevokeStrikeCommand(strike.StrikeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        strike.Revoked.Should().BeFalse();
    }

    [Fact]
    public async Task Unrevoke_ReturnsNotFound_WhenStrikeMissing()
    {
        var id = Guid.NewGuid();
        _strikes.ReadForUpdateByIdAsync(id, Arg.Any<CancellationToken>()).Returns((ClubMemberStrike?)null);

        var result = await CreateUnrevokeHandler().Handle(new UnrevokeStrikeCommand(id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("strike.not_found");
    }
}

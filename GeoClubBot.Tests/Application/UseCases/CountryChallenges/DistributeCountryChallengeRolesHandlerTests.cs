using FluentAssertions;
using MediatR;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.Users;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

public sealed class DistributeCountryChallengeRolesHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IDiscordServerRolesAccess _roles = Substitute.For<IDiscordServerRolesAccess>();
    private readonly List<string> _calls = [];

    public DistributeCountryChallengeRolesHandlerTests()
    {
        _mediator.Send(Arg.Any<GeoGuessrUserIdsToDiscordUserIdsQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<GeoGuessrUserIdsToDiscordUserIdsQuery>().GeoGuessrUserIds
                .Select(id => (ulong)id.Length)
                .ToList());
        _roles.When(r => r.RemoveRoleFromAllPlayersAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()))
            .Do(call => _calls.Add($"remove {call.ArgAt<ulong>(0)}"));
        _roles.When(r => r.AddRoleToMembersByUserIdsAsync(Arg.Any<IEnumerable<ulong>>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>()))
            .Do(call => _calls.Add($"add {call.ArgAt<ulong>(1)} to {string.Join(",", call.ArgAt<IEnumerable<ulong>>(0))}"));
    }

    [Fact]
    public async Task Handle_TakesEachRoleOnce_BeforeHandingAnyOut()
    {
        // One "podium" role for the top two, a second role for third. Ids map to their length.
        var assignment = new RoleAssignment([7, 7, 8], [["a"], ["bb"], ["ccc"]]);

        await new DistributeCountryChallengeRolesHandler(_mediator, _roles)
            .Handle(new DistributeCountryChallengeRolesCommand(assignment), CancellationToken.None);

        _calls.Should().Equal("remove 7", "remove 8", "add 7 to 1", "add 7 to 2", "add 8 to 3");
    }

    [Fact]
    public async Task Handle_SkipsPlacesNobodyHolds()
    {
        var assignment = new RoleAssignment([7, 8], [["a", "b"], []]);

        await new DistributeCountryChallengeRolesHandler(_mediator, _roles)
            .Handle(new DistributeCountryChallengeRolesCommand(assignment), CancellationToken.None);

        _calls.Should().Equal("remove 7", "remove 8", "add 7 to 1,1");
        await _mediator.Received(1).Send(Arg.Any<GeoGuessrUserIdsToDiscordUserIdsQuery>(), Arg.Any<CancellationToken>());
    }
}

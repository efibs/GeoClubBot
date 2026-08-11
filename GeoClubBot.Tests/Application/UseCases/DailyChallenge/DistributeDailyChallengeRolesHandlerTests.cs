using Configuration;
using Entities;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.DailyChallenge;
using UseCases.UseCases.Users;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.DailyChallengeTests;

/// <summary>
/// Covers which players end up on which podium role. Roles are handed out per challenge, but a
/// player only ever earns one of them: the highest-priority challenge they placed in wins, and the
/// players behind them move up on the lower-priority leaderboards.
/// </summary>
public sealed class DistributeDailyChallengeRolesHandlerTests
{
    private const ulong FirstRoleId = 100;
    private const ulong SecondRoleId = 200;
    private const ulong ThirdRoleId = 300;

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IDiscordServerRolesAccess _roles = Substitute.For<IDiscordServerRolesAccess>();

    /// <summary>The user ids the handler asked to resolve, keyed by the role they were resolved for.</summary>
    private readonly Dictionary<ulong, List<string>> _resolvedPerRole = new();

    public DistributeDailyChallengeRolesHandlerTests()
    {
        // The queries are issued in first/second/third order, so the call index identifies the role.
        var roleIdsInCallOrder = new[] { FirstRoleId, SecondRoleId, ThirdRoleId };
        _mediator.Send(Arg.Any<GeoGuessrUserIdsToDiscordUserIdsQuery>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var query = callInfo.ArgAt<GeoGuessrUserIdsToDiscordUserIdsQuery>(0);
                _resolvedPerRole[roleIdsInCallOrder[_resolvedPerRole.Count]] = query.GeoGuessrUserIds.ToList();
                return Task.FromResult(new List<ulong>());
            });
    }

    private DistributeDailyChallengeRolesHandler CreateHandler() =>
        new(_mediator, _roles, Options.Create(new DailyChallengesConfiguration
        {
            Schedule = "0 0 0 * * ?",
            TextChannelId = 5,
            ConfigurationFilePath = "challenges.json",
            FirstRoleId = FirstRoleId,
            SecondRoleId = SecondRoleId,
            ThirdRoleId = ThirdRoleId,
        }));

    private static ClubChallengeResultPlayer Player(string userId) =>
        new(userId, $"nick-{userId}", "5000 points", "1000m");

    private async Task DistributeAsync(params ClubChallengeResult[] results) =>
        await CreateHandler().Handle(new DistributeDailyChallengeRolesCommand([.. results]), CancellationToken.None);

    [Fact]
    public async Task Handle_AwardsThePodiumOfEveryChallenge()
    {
        await DistributeAsync(new ClubChallengeResult("Easy", 1, [Player("a"), Player("b"), Player("c"), Player("d")]));

        _resolvedPerRole[FirstRoleId].Should().Equal("a");
        _resolvedPerRole[SecondRoleId].Should().Equal("b");
        _resolvedPerRole[ThirdRoleId].Should().Equal("c");
    }

    [Fact]
    public async Task Handle_StartsWithTheHighestRolePriority_RegardlessOfResultOrder()
    {
        await DistributeAsync(
            new ClubChallengeResult("Easy", 1, [Player("winner"), Player("a")]),
            new ClubChallengeResult("Hard", 3, [Player("winner")]),
            new ClubChallengeResult("Medium", 2, [Player("winner"), Player("b")]));

        // The winner takes the hard challenge and is skipped on the others, so b and a move up.
        _resolvedPerRole[FirstRoleId].Should().Equal("winner", "b", "a");
        _resolvedPerRole[SecondRoleId].Should().BeEmpty();
    }

    /// <summary>
    /// Players used to be de-duplicated by comparing their nickname against the set of already
    /// awarded *user ids*, which never matched — so a player could collect several podium roles and,
    /// worse, be synced twice into the same unit of work.
    /// </summary>
    [Fact]
    public async Task Handle_AwardsEachPlayerAtMostOneRole()
    {
        await DistributeAsync(
            new ClubChallengeResult("Hard", 2, [Player("a")]),
            new ClubChallengeResult("Easy", 1, [Player("b"), Player("a"), Player("c")]));

        _resolvedPerRole[FirstRoleId].Should().Equal("a", "b");
        // a already won the hard challenge, so b moves up to first and c to second on the easy one.
        _resolvedPerRole[SecondRoleId].Should().Equal("c");
        _resolvedPerRole[ThirdRoleId].Should().BeEmpty();

        _resolvedPerRole.Values.SelectMany(ids => ids).Should().OnlyHaveUniqueItems(
            "resolving the same player twice would insert them twice into the same unit of work");
    }

    /// <summary>Two challenges can share a role priority; the dedup still has to hold across them.</summary>
    [Fact]
    public async Task Handle_AwardsEachPlayerAtMostOneRole_AcrossChallengesWithTheSamePriority()
    {
        await DistributeAsync(
            new ClubChallengeResult("Hard", 1, [Player("a")]),
            new ClubChallengeResult("Easy", 1, [Player("b"), Player("a")]));

        _resolvedPerRole[FirstRoleId].Should().Equal("a", "b");
        _resolvedPerRole[SecondRoleId].Should().BeEmpty();
        _resolvedPerRole.Values.SelectMany(ids => ids).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Handle_ClearsThePreviousRoleHolders_BeforeAwardingTheNewOnes()
    {
        await DistributeAsync(new ClubChallengeResult("Easy", 1, []));

        await _roles.Received().RemoveRoleFromAllPlayersAsync(FirstRoleId, Arg.Any<CancellationToken>());
        await _roles.Received().RemoveRoleFromAllPlayersAsync(SecondRoleId, Arg.Any<CancellationToken>());
        await _roles.Received().RemoveRoleFromAllPlayersAsync(ThirdRoleId, Arg.Any<CancellationToken>());
        _resolvedPerRole.Values.Should().OnlyContain(ids => ids.Count == 0);
    }
}

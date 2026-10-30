using Configuration;
using Entities;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.Users;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class ClubMemberActivityRewardHandlerTests
{
    private const ulong RewardChannel = 321;
    private const ulong MvpRole = 654;

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IDiscordServerRolesAccess _roles = Substitute.For<IDiscordServerRolesAccess>();
    private readonly IDiscordMessageAccess _messages = Substitute.For<IDiscordMessageAccess>();
    private readonly ActivityRewardConfiguration _config = new() { TextChannelId = RewardChannel, MvpRoleId = MvpRole };

    private string? _posted;
    private IEnumerable<string> _mvpUserIds = [];
    private List<ulong>? _rolesAdded;
    private List<ulong>? _rolesRemoved;

    public ClubMemberActivityRewardHandlerTests()
    {
        _messages
            .When(m => m.SendMessageAsync(Arg.Any<string>(), RewardChannel, Arg.Any<CancellationToken>()))
            .Do(call => _posted = call.Arg<string>());

        _mediator.Send(Arg.Any<GeoGuessrUserIdsToDiscordUserIdsQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _mvpUserIds = call.Arg<GeoGuessrUserIdsToDiscordUserIdsQuery>().GeoGuessrUserIds.ToList();
                return _mvpUserIds.Select(DiscordIdOf).ToList();
            });

        _roles.ReadMembersWithRoleAsync(MvpRole, Arg.Any<CancellationToken>()).Returns(new List<ulong>());
        _roles.When(r => r.AddRoleToMembersByUserIdsAsync(Arg.Any<IEnumerable<ulong>>(), MvpRole, Arg.Any<CancellationToken>()))
            .Do(call => _rolesAdded = call.Arg<IEnumerable<ulong>>().ToList());
        _roles.When(r => r.RemoveRoleFromPlayersAsync(Arg.Any<IEnumerable<ulong>>(), MvpRole, Arg.Any<CancellationToken>()))
            .Do(call => _rolesRemoved = call.Arg<IEnumerable<ulong>>().ToList());
    }

    private static ulong DiscordIdOf(string userId) => userId switch
    {
        "a" => 1,
        "b" => 2,
        "c" => 3,
        _ => 9
    };

    private static ClubMemberActivityStatus Status(string userId, int rawXp, int? ruleXp = null) =>
        new(userId.ToUpperInvariant(), userId, true, rawXp, 0, false, 0, null, ruleXp);

    private Task HandleAsync(params ClubMemberActivityStatus[] statuses) =>
        new ClubMemberActivityRewardHandler(
                _mediator,
                _roles,
                _messages,
                NullLogger<ClubMemberActivityRewardHandler>.Instance,
                Options.Create(_config))
            .Handle(new ClubMemberActivityRewardCommand([.. statuses]), CancellationToken.None);

    [Fact]
    public async Task ListsTheBestThreeXpAmounts_BestFirst()
    {
        await HandleAsync(Status("c", 30), Status("a", 100), Status("d", 10), Status("b", 50));

        _posted.Should().Be(
            "# The new MVP's of the club are here! :partying_face:\n" +
            $"The club members that achieved the most club XP since last time are:{Environment.NewLine}" +
            $"**#1:** With 100XP: A (Our new MVP) :clap: {Environment.NewLine}" +
            $"**#2:** With 50XP: B{Environment.NewLine}" +
            $"**#3:** With 30XP: C{Environment.NewLine}");
    }

    [Fact]
    public async Task MembersWithTheSameXp_ShareAPlace_AndTheFirstPlaceIsPlural()
    {
        await HandleAsync(Status("a", 100), Status("b", 100), Status("c", 40));

        _posted.Should().Contain("**#1:** With 100XP: A, B (Our new MVP's) :clap: ")
            .And.Contain("**#2:** With 40XP: C");
        _mvpUserIds.Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task OnlyTheFirstPlace_BecomesMvp()
    {
        await HandleAsync(Status("a", 100), Status("b", 50));

        _mvpUserIds.Should().Equal("a");
    }

    [Fact]
    public async Task MembersWithoutXp_AreLeftOut()
    {
        await HandleAsync(Status("a", 10), Status("b", 0));

        _posted.Should().Contain("A").And.NotContain("B (").And.NotContain("#2");
    }

    [Fact]
    public async Task RuleXpDecides_ByDefault()
    {
        await HandleAsync(Status("a", rawXp: 200, ruleXp: 20), Status("b", rawXp: 50, ruleXp: 50));

        _mvpUserIds.Should().Equal("b");
    }

    [Fact]
    public async Task RawXpDecides_WhenConfiguredSo()
    {
        _config.UseRuleXp = false;

        await HandleAsync(Status("a", rawXp: 200, ruleXp: 20), Status("b", rawXp: 50, ruleXp: 50));

        _mvpUserIds.Should().Equal("a");
    }

    [Fact]
    public async Task MvpRole_MovesToTheNewMvp_WithoutTouchingOneWhoKeepsIt()
    {
        _roles.ReadMembersWithRoleAsync(MvpRole, Arg.Any<CancellationToken>()).Returns(new List<ulong> { 1, 2 });

        await HandleAsync(Status("a", 100), Status("c", 100));

        _rolesAdded.Should().Equal(3UL);
        _rolesRemoved.Should().Equal(2UL);
    }

    [Fact]
    public async Task WeekWithoutXp_PostsNothing_AndTakesTheMvpRoleAway()
    {
        _roles.ReadMembersWithRoleAsync(MvpRole, Arg.Any<CancellationToken>()).Returns(new List<ulong> { 1 });

        await HandleAsync(Status("a", 0));

        _posted.Should().BeNull();
        _rolesAdded.Should().BeEmpty();
        _rolesRemoved.Should().Equal(1UL);
    }

    [Fact]
    public async Task MvpRoleStillMoves_WhenTheAnnouncementFails()
    {
        _messages.SendMessageAsync(Arg.Any<string>(), RewardChannel, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("discord is down"));

        await HandleAsync(Status("a", 100));

        _rolesAdded.Should().Equal(1UL);
    }
}

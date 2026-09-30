using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.InactiveMembers;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Application.UseCases.InactiveMembersTests;

public sealed class GetTodaysInactiveMembersHandlerTests
{
    private static readonly Guid MainClub = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondClub = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();
    private readonly IClubMissionBoardReader _boardReader = Substitute.For<IClubMissionBoardReader>();

    public GetTodaysInactiveMembersHandlerTests()
    {
        _members.ReadClubMembersByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _activityReader.ReadTodaysActivitiesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _clubs.ReadClubByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Entities.Club?)null);
        _boardReader.ReadCurrentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ClubMissionBoardWeek?)null);
    }

    // The first club id is flagged IsMain, matching how GeoGuessrConfiguration resolves the main club.
    private GetTodaysInactiveMembersHandler CreateHandler(params Guid[] clubIds) => new(
        _members,
        _clubs,
        _activityReader,
        _boardReader,
        ClubActivities.Classifier(),
        Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 0 * * ?",
            ActivityNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = clubIds
                .Select((id, i) => new GeoGuessrClubEntry { ClubId = id, NcfaToken = "x", IsMain = i == 0 })
                .ToList(),
        }),
        Options.Create(new MissionBoardConfiguration()));

    [Fact]
    public async Task Handle_ReportsStreakAndClaimInactivitySeparately()
    {
        var claimed = new ClubMemberBuilder().WithUserId("u-claimed").WithNickname("Zeta").InClub(MainClub).Build();
        var idle = new ClubMemberBuilder().WithUserId("u-idle").WithNickname("Alpha").InClub(MainClub).Build();
        var played = new ClubMemberBuilder().WithUserId("u-played").WithNickname("Mike").InClub(MainClub).Build();

        _members.ReadClubMembersByClubIdAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns([claimed, idle, played]);

        _activityReader.ReadTodaysActivitiesAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns([ClubActivities.Challenge(played.UserId)]);

        var tiles = FreeTiles(9);
        tiles[0] = Tile(claimedBy: claimed.UserId, claimedAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        _boardReader.ReadCurrentAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, tiles), nextClaimResetAt: DateTimeOffset.UtcNow.AddHours(5)));

        var result = await CreateHandler(MainClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMembers.Should().Be(3);
        // Each list is scoped to its own activity, so a member appears in one, both, or neither.
        result.Value.ChallengeInactive.Select(m => m.Nickname).Should().Equal("Alpha", "Zeta");
        result.Value.ClaimInactive!.Select(m => m.Nickname).Should().Equal("Alpha", "Mike");
        result.Value.FreeMissions.Should().Be(8);
    }

    [Fact]
    public async Task Handle_ListsNobodyForClaims_WhenNoMissionIsFree()
    {
        var member = new ClubMemberBuilder().WithUserId("u-a").WithNickname("Alpha").InClub(MainClub).Build();
        _members.ReadClubMembersByClubIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns([member]);
        _boardReader.ReadCurrentAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, Tile(claimedBy: "someone-else"))));

        var result = await CreateHandler(MainClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        result.Value.ClaimInactive.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_LeavesTheClaimListUnknown_WhenTheBoardCannotBeRead()
    {
        var member = new ClubMemberBuilder().WithUserId("u-a").InClub(MainClub).Build();
        _members.ReadClubMembersByClubIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns([member]);

        var result = await CreateHandler(MainClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        result.Value.ClaimInactive.Should().BeNull();
        result.Value.FreeMissions.Should().BeNull();
    }

    [Fact]
    public async Task Handle_IgnoresActivityThatDoesNotKeepTheStreak()
    {
        var member = new ClubMemberBuilder().WithUserId("u-a").WithNickname("Alpha").InClub(MainClub).Build();
        _members.ReadClubMembersByClubIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns([member]);

        _activityReader.ReadTodaysActivitiesAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns([
                // A board mission and a zero-XP club challenge say nothing about the streak.
                ClubActivities.BoardMission(member.UserId),
                ClubActivities.ClubChallenge(member.UserId),
            ]);

        var result = await CreateHandler(MainClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        result.Value.ChallengeInactive.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_DefaultsToMainClub_WhenNoClubIsGiven()
    {
        await CreateHandler(MainClub, SecondClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        await _activityReader.Received(1).ReadTodaysActivitiesAsync(MainClub, Arg.Any<CancellationToken>());
        await _activityReader.DidNotReceive().ReadTodaysActivitiesAsync(SecondClub, Arg.Any<CancellationToken>());
        await _boardReader.Received(1).ReadCurrentAsync(MainClub, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UsesTheGivenClub_WhenOneIsSpecified()
    {
        await CreateHandler(MainClub, SecondClub).Handle(new GetTodaysInactiveMembersQuery(SecondClub), CancellationToken.None);

        await _activityReader.Received(1).ReadTodaysActivitiesAsync(SecondClub, Arg.Any<CancellationToken>());
        await _activityReader.DidNotReceive().ReadTodaysActivitiesAsync(MainClub, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenTheClubIsNotConfigured()
    {
        var unknown = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var result = await CreateHandler(MainClub).Handle(new GetTodaysInactiveMembersQuery(unknown), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_CarriesTheDiscordUserId_ForLinkedMembers()
    {
        var linked = new ClubMemberBuilder().WithUserId("u-linked").WithNickname("Linked").WithDiscordUserId(4242UL).InClub(MainClub).Build();
        var unlinked = new ClubMemberBuilder().WithUserId("u-unlinked").WithNickname("Unlinked").InClub(MainClub).Build();
        _members.ReadClubMembersByClubIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns([linked, unlinked]);

        var result = await CreateHandler(MainClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        result.Value.ChallengeInactive.Single(m => m.Nickname == "Linked").DiscordUserId.Should().Be(4242UL);
        result.Value.ChallengeInactive.Single(m => m.Nickname == "Unlinked").DiscordUserId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UsesTheClubNameFromTheDatabase()
    {
        _clubs.ReadClubByIdAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns(Entities.Club.Create(MainClub, "Awesome Club", level: 5));

        var result = await CreateHandler(MainClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        result.Value.ClubName.Should().Be("Awesome Club");
    }
}

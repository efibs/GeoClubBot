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

namespace GeoClubBot.Tests.Application.UseCases.InactiveMembersTests;

public sealed class GetTodaysInactiveMembersHandlerTests
{
    private static readonly Guid MainClub = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondClub = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const int DailyMissionXpReward = 20;

    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();

    public GetTodaysInactiveMembersHandlerTests()
    {
        _members.ReadClubMembersByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _activityReader.ReadTodaysActivitiesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _clubs.ReadClubByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Entities.Club?)null);
    }

    // The first club id is flagged IsMain, matching how GeoGuessrConfiguration resolves the main club.
    private GetTodaysInactiveMembersHandler CreateHandler(params Guid[] clubIds) => new(
        _members,
        _clubs,
        _activityReader,
        Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 0 * * ?",
            ActivityNcfaToken = "x",
            MissionsNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = clubIds
                .Select((id, i) => new GeoGuessrClubEntry { ClubId = id, NcfaToken = "x", IsMain = i == 0 })
                .ToList(),
        }),
        Options.Create(new DailyMissionReminderConfiguration
        {
            Schedule = "0 * * * * ?",
            DefaultMessage = "x",
            DailyMissionXpReward = DailyMissionXpReward
        }));

    private static ReadClubActivitiesItemDto Activity(string userId, int xpReward) => new()
    {
        UserId = userId,
        XpReward = xpReward,
        RecordedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_ReturnsRosterMinusMembersWhoCompletedTheDailyMissionToday()
    {
        var done = new ClubMemberBuilder().WithUserId("u-done").WithNickname("Zeta").InClub(MainClub).Build();
        var idle = new ClubMemberBuilder().WithUserId("u-idle").WithNickname("Alpha").InClub(MainClub).Build();
        var playedNoMission = new ClubMemberBuilder().WithUserId("u-played").WithNickname("Mike").InClub(MainClub).Build();

        _members.ReadClubMembersByClubIdAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns([done, idle, playedNoMission]);

        _activityReader.ReadTodaysActivitiesAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns([
                Activity(done.UserId, DailyMissionXpReward),
                // Non-mission XP (e.g. a regular game) does not count as completing the daily mission.
                Activity(playedNoMission.UserId, 150),
            ]);

        var result = await CreateHandler(MainClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMembers.Should().Be(3);
        // "done" is excluded; the rest are ordered by nickname.
        result.Value.Members.Select(m => m.Nickname).Should().Equal("Alpha", "Mike");
    }

    [Fact]
    public async Task Handle_DefaultsToMainClub_WhenNoClubIsGiven()
    {
        await CreateHandler(MainClub, SecondClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        await _activityReader.Received(1).ReadTodaysActivitiesAsync(MainClub, Arg.Any<CancellationToken>());
        await _activityReader.DidNotReceive().ReadTodaysActivitiesAsync(SecondClub, Arg.Any<CancellationToken>());
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
    public async Task Handle_ReturnsNoInactiveMembers_WhenEveryoneCompletedTheMission()
    {
        var a = new ClubMemberBuilder().WithUserId("u-a").InClub(MainClub).Build();
        _members.ReadClubMembersByClubIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns([a]);
        _activityReader.ReadTodaysActivitiesAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns([Activity(a.UserId, DailyMissionXpReward)]);

        var result = await CreateHandler(MainClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        result.Value.Members.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CarriesTheDiscordUserId_ForLinkedMembers()
    {
        var linked = new ClubMemberBuilder().WithUserId("u-linked").WithNickname("Linked").WithDiscordUserId(4242UL).InClub(MainClub).Build();
        var unlinked = new ClubMemberBuilder().WithUserId("u-unlinked").WithNickname("Unlinked").InClub(MainClub).Build();
        _members.ReadClubMembersByClubIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns([linked, unlinked]);

        var result = await CreateHandler(MainClub).Handle(new GetTodaysInactiveMembersQuery(null), CancellationToken.None);

        result.Value.Members.Single(m => m.Nickname == "Linked").DiscordUserId.Should().Be(4242UL);
        result.Value.Members.Single(m => m.Nickname == "Unlinked").DiscordUserId.Should().BeNull();
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

using Configuration;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Club;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.ClubActivities;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Application.UseCases.Club;

public sealed class ClubQueriesHandlerTests
{
    private static readonly Guid MainClub = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly global::Entities.Club Main = global::Entities.Club.Create(MainClub, "Main", 1);
    private static readonly global::Entities.Club Other = global::Entities.Club.Create(Guid.NewGuid(), "Other", 1);

    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IGeoGuessrActivityReader _activities = Substitute.For<IGeoGuessrActivityReader>();
    private readonly IClubMissionBoardReader _boards = Substitute.For<IClubMissionBoardReader>();

    public ClubQueriesHandlerTests()
    {
        _clubs.ReadClubByIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns(Main);
        _clubs.ReadClubByNameAsync("Other", Arg.Any<CancellationToken>()).Returns(Other);
        _activities.ReadTodaysActivitiesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _boards.ReadCurrentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Entities.ClubMissionBoardWeek?)null);
        _members.ReadClubMembersByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private ClubQueriesHandler CreateHandler() =>
        new(
            _clubs,
            _members,
            _activities,
            _boards,
            Classifier(),
            new GeoGuessrConfigurationBuilder().WithClub(MainClub).BuildOptions(),
            Options.Create(new MissionBoardConfiguration()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoClubName_MeansTheMainClub(string? name)
    {
        var club = await CreateHandler().Handle(new GetClubByNameOrDefaultQuery(name), CancellationToken.None);

        club.Should().BeSameAs(Main);
    }

    [Fact]
    public async Task AClubIsFoundByName()
    {
        var club = await CreateHandler().Handle(new GetClubByNameOrDefaultQuery("Other"), CancellationToken.None);

        club.Should().BeSameAs(Other);
    }

    [Fact]
    public async Task AllClubs_AreRead()
    {
        _clubs.ReadAllClubsAsync(Arg.Any<CancellationToken>()).Returns([Main, Other]);

        var clubs = await CreateHandler().Handle(new GetAllClubsQuery(), CancellationToken.None);

        clubs.Should().Equal(Main, Other);
    }

    [Fact]
    public async Task TodaysXp_OfAnUnknownClub_IsEmpty()
    {
        var result = await CreateHandler().Handle(new GetClubTodaysXpQuery("Nope"), CancellationToken.None);

        result.Should().Be(new GetClubTodaysXpResult(null, null, null, null, null, null));
    }

    [Theory]
    [InlineData(null, "Main")]
    [InlineData(" ", "Main")]
    [InlineData("Other", "Other")]
    public async Task TodaysXp_IsReadForTheNamedClub_OrTheMainOne(string? name, string expected)
    {
        var result = await CreateHandler().Handle(new GetClubTodaysXpQuery(name), CancellationToken.None);

        result.ClubName.Should().Be(expected);
    }

    [Fact]
    public async Task TodaysXp_SumsEverything_ButCountsTheStreakPerMember()
    {
        _activities.ReadTodaysActivitiesAsync(MainClub, Arg.Any<CancellationToken>()).Returns(
        [
            Challenge("alice"),
            Challenge("alice"),
            Challenge("bob"),
            BoardMission("carol"),
            BoardBonus("carol"),
            ClubChallenge("dave")
        ]);

        var result = await CreateHandler().Handle(new GetClubTodaysXpQuery(null), CancellationToken.None);

        result.Xp.Should().Be(20 + 20 + 20 + 20 + 100);
        result.ChallengeMemberCount.Should().Be(2);
        result.BoardMissionCount.Should().Be(1);
    }

    [Fact]
    public async Task TodaysXp_CountsTheTrackedMembers()
    {
        _members.ReadClubMembersByClubIdAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns([new ClubMemberBuilder().Build(), new ClubMemberBuilder().WithUserId("b").Build()]);

        var result = await CreateHandler().Handle(new GetClubTodaysXpQuery(null), CancellationToken.None);

        result.TotalMemberCount.Should().Be(2);
    }

    [Fact]
    public async Task TodaysXp_WithoutABoard_HasNoClaimCount()
    {
        var result = await CreateHandler().Handle(new GetClubTodaysXpQuery(null), CancellationToken.None);

        result.ClaimMemberCount.Should().BeNull();
    }

    [Fact]
    public async Task TodaysXp_CountsMembersWhoClaimedThisCycle_OnceEach()
    {
        var nextReset = DateTimeOffset.UtcNow.AddHours(5);
        var cycleStart = nextReset.AddDays(-1);
        _boards.ReadCurrentAsync(MainClub, Arg.Any<CancellationToken>()).Returns(Week(
            Board(1,
                Tile(claimedBy: "alice", claimedAt: cycleStart),
                Tile(index: 1, claimedBy: "alice", claimedAt: cycleStart.AddHours(1)),
                Tile(index: 2, claimedBy: "bob", claimedAt: cycleStart.AddHours(2)),
                Tile(index: 3, claimedBy: "carol", claimedAt: cycleStart.AddSeconds(-1)),
                Tile(index: 4)),
            nextClaimResetAt: nextReset));

        var result = await CreateHandler().Handle(new GetClubTodaysXpQuery(null), CancellationToken.None);

        result.ClaimMemberCount.Should().Be(2);
    }
}

using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.MissionBoard;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Application.UseCases.MissionBoard;

public sealed class GetClubMissionBoardHandlerTests
{
    private static readonly Guid MainClub = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SecondClub = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IClubMissionBoardReader _boards = Substitute.For<IClubMissionBoardReader>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();

    public GetClubMissionBoardHandlerTests()
    {
        _members.ReadClubMembersByUserIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private Task<Result<ClubMissionBoardView>> HandleAsync(Guid? clubId) =>
        new GetClubMissionBoardHandler(
                _boards,
                _clubs,
                _members,
                new GeoGuessrConfigurationBuilder().WithClub(MainClub).WithClub(SecondClub, isMain: false).BuildOptions(),
                Options.Create(new MissionBoardConfiguration()))
            .Handle(new GetClubMissionBoardQuery(clubId), CancellationToken.None);

    [Fact]
    public async Task AClubTheBotDoesNotTrack_IsNotFound()
    {
        var result = await HandleAsync(Guid.NewGuid());

        result.Error.Code.Should().Be("club.not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
        _boards.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ABoardThatCannotBeRead_IsNotFound()
    {
        _boards.ReadCurrentAsync(SecondClub, Arg.Any<CancellationToken>()).Returns((ClubMissionBoardWeek?)null);

        var result = await HandleAsync(SecondClub);

        result.Error.Code.Should().Be("mission_board.unavailable");
    }

    [Fact]
    public async Task NoClub_MeansTheMainClub()
    {
        var week = Week(Board(1, Tile()));
        _boards.ReadCurrentAsync(MainClub, Arg.Any<CancellationToken>()).Returns(week);
        _clubs.ReadClubByIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns(global::Entities.Club.Create(MainClub, "Main", 1));

        var view = (await HandleAsync(null)).Value;

        view.ClubId.Should().Be(MainClub);
        view.ClubName.Should().Be("Main");
        view.Board.Should().BeSameAs(week);
    }

    [Fact]
    public async Task AClubWithoutARecord_IsCalledByItsId()
    {
        _boards.ReadCurrentAsync(SecondClub, Arg.Any<CancellationToken>()).Returns(Week(Board(1, Tile())));
        _clubs.ReadClubByIdAsync(SecondClub, Arg.Any<CancellationToken>()).Returns((global::Entities.Club?)null);

        var view = (await HandleAsync(SecondClub)).Value;

        view.ClubName.Should().Be(SecondClub.ToString());
    }

    [Fact]
    public async Task LooksUpEveryClaimerAndHelperOnce_AndNamesThem()
    {
        _boards.ReadCurrentAsync(MainClub, Arg.Any<CancellationToken>()).Returns(Week(Board(1,
            Tile(claimedBy: "alice", helpers: ["bob", "alice"]),
            Tile(index: 1, helpers: ["bob"]))));
        _members.ReadClubMembersByUserIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ClubMember>
            {
                ["alice"] = new ClubMemberBuilder().WithUserId("alice").WithNickname("Alice").Build()
            });

        var view = (await HandleAsync(null)).Value;

        await _members.Received(1).ReadClubMembersByUserIdsAsync(
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.Count == 2 && ids.Contains("alice") && ids.Contains("bob")),
            Arg.Any<CancellationToken>());
        view.NicknameOf("alice").Should().Be("Alice");
        view.NicknameOf("bob").Should().Be("bob");
    }

    [Fact]
    public async Task ClaimCycleStart_ComesFromTheBoardsReset()
    {
        var nextReset = DateTimeOffset.UtcNow.AddHours(5);
        _boards.ReadCurrentAsync(MainClub, Arg.Any<CancellationToken>()).Returns(Week(Board(1, Tile()), nextClaimResetAt: nextReset));

        var view = (await HandleAsync(null)).Value;

        view.ClaimCycleStart.Should().Be(nextReset.AddDays(-1));
    }
}

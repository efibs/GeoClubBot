using FluentAssertions;
using GeoClubBot.MockGeoGuessr.Client;
using GeoClubBot.MockGeoGuessr.DataStore;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using Xunit;

namespace GeoClubBot.Tests.Tools;

/// <summary>
/// The mock's mission board has to behave like GeoGuessr's for the board features to be tried out
/// locally: five boards unlocked in turn, one open claim per member, and the feed entries a
/// completed mission and a cleared board produce.
/// </summary>
public sealed class MockGeoGuessrMissionBoardTests
{
    private static readonly Guid ClubId = Guid.NewGuid();
    private readonly MockGeoGuessrDataStore _store = new();

    [Fact]
    public async Task TheBoard_HasFiveBoardsOfTheRealSizes_AndOnlyTheClubsClientSeesIt()
    {
        var board = await new MockGeoGuessrClient(_store, ClubId).ReadClubMissionBoardAsync();

        board.Boards.Select(b => b.Tiles.Count).Should().Equal(9, 16, 25, 25, 25);
        board.CurrentBoardNumber.Should().Be(1);
        board.PeriodStart.DayOfWeek.Should().Be(DayOfWeek.Wednesday);
        board.You!.NextDayAt.Should().BeAfter(DateTimeOffset.UtcNow);
        board.Boards.SelectMany(b => b.Tiles).Should().OnlyContain(t => board.Templates.ContainsKey(t.TemplateId));

        var withoutClub = () => new MockGeoGuessrClient(_store).ReadClubMissionBoardAsync();
        await withoutClub.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void AMember_CanHoldOnlyOneOpenMission()
    {
        var board = _store.GetMissionBoard(ClubId);
        board.Claim("u1");

        var second = () => board.Claim("u1");

        second.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CompletingEveryMissionOfABoard_CreditsTheMissionsAndTheBonus_AndUnlocksTheNextBoard()
    {
        var board = _store.GetMissionBoard(ClubId);

        var entries = new List<(string UserId, int Xp, int Type)>();
        for (var i = 0; i < 9; i++)
        {
            var tile = board.Claim($"u{i}");
            entries.AddRange(board.Complete(tile.MissionId));
        }

        entries.Count(e => e.Type == 5).Should().Be(9);
        entries.Should().ContainSingle(e => e.Type == 6).Which.Should().Be(("u8", 100, 6));

        var current = board.Current;
        current.CurrentBoardNumber.Should().Be(2);
        current.Boards[0].ClearedAt.Should().NotBeNull();
        ClubMissionBoardAssembler.AssembleEntity(current).FreeTiles.Should().HaveCount(16);
    }

    [Fact]
    public void Reset_KeepsTheFinishedWeekAsThePreviousOne()
    {
        var board = _store.GetMissionBoard(ClubId);
        var tile = board.Claim("u1");
        board.AddHelper(tile.MissionId, "u2");

        board.Reset(DateTimeOffset.UtcNow);

        board.Previous!.Boards[0].Tiles.Single(t => t.MissionId == tile.MissionId).Helpers.Should().Equal("u2");
        board.Current.Boards.SelectMany(b => b.Tiles).Should().OnlyContain(t => t.ClaimedBy == null);
    }

    [Fact]
    public void Snapshots_AreCopies_SoReadersNeverSeeLaterChanges()
    {
        var board = _store.GetMissionBoard(ClubId);
        var before = board.Current;

        board.Claim("u1");

        before.Boards[0].Tiles.Should().OnlyContain(t => t.ClaimedBy == null);
    }
}

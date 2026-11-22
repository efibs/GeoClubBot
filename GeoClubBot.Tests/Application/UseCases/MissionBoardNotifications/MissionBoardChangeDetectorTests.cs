using Entities;
using FluentAssertions;
using UseCases.UseCases.MissionBoardNotifications;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;

namespace GeoClubBot.Tests.Application.UseCases.MissionBoardNotifications;

public sealed class MissionBoardChangeDetectorTests
{
    private static readonly Guid ClubId = Guid.NewGuid();

    private static ClubMissionBoardObservation Seen(ClubMissionBoardWeek week) =>
        ClubMissionBoardObservation.Create(ClubId, week, DateTimeOffset.UtcNow);

    private static ClubMissionBoardWeek OnBoard(int number, DateTimeOffset? periodStart = null, bool allCleared = false, int free = 4)
    {
        var week = Week(Board(number, FreeTiles(free, number)), allBoardsCleared: allCleared);
        return periodStart is { } start ? week with { PeriodStart = start, PeriodEnd = start.AddDays(7) } : week;
    }

    [Fact]
    public void FirstLook_IsStored_ButNotTold()
    {
        var change = MissionBoardChangeDetector.Detect(null, OnBoard(2));

        change.Kind.Should().Be(MissionBoardChangeKind.FirstSeen);
        change.ShouldStore.Should().BeTrue();
        change.ShouldNotify.Should().BeFalse();
    }

    [Fact]
    public void SameBoard_ChangesNothing()
    {
        var change = MissionBoardChangeDetector.Detect(Seen(OnBoard(2)), OnBoard(2));

        change.ShouldStore.Should().BeFalse();
        change.ShouldNotify.Should().BeFalse();
    }

    [Fact]
    public void NextBoard_IsAnUnlock()
    {
        MissionBoardChangeDetector.Detect(Seen(OnBoard(2)), OnBoard(3)).Kind
            .Should().Be(MissionBoardChangeKind.BoardUnlocked);
    }

    [Fact]
    public void BoardsSkippedWhileDown_AreOneUnlock()
    {
        MissionBoardChangeDetector.Detect(Seen(OnBoard(1)), OnBoard(4)).Kind
            .Should().Be(MissionBoardChangeKind.BoardUnlocked);
    }

    [Fact]
    public void LaterWeek_IsANewWeek_WhateverItsBoard()
    {
        var change = MissionBoardChangeDetector.Detect(Seen(OnBoard(5)), OnBoard(1, PeriodStart.AddDays(7)));

        change.Kind.Should().Be(MissionBoardChangeKind.NewWeek);
        change.ShouldNotify.Should().BeTrue();
    }

    [Fact]
    public void OlderRead_IsIgnored_SoTheStateOnlyMovesForward()
    {
        MissionBoardChangeDetector.Detect(Seen(OnBoard(3)), OnBoard(2)).ShouldStore.Should().BeFalse();
        MissionBoardChangeDetector.Detect(Seen(OnBoard(1, PeriodStart.AddDays(7))), OnBoard(5)).ShouldStore.Should().BeFalse();
    }

    [Fact]
    public void LastBoardCleared_IsStored_ButNotTold()
    {
        var change = MissionBoardChangeDetector.Detect(Seen(OnBoard(5)), OnBoard(5, allCleared: true));

        change.Kind.Should().Be(MissionBoardChangeKind.Updated);
        change.ShouldNotify.Should().BeFalse();
    }

    [Fact]
    public void UnlockWithNothingFree_IsNotTold()
    {
        var change = MissionBoardChangeDetector.Detect(Seen(OnBoard(2)), OnBoard(3, free: 0));

        change.ShouldStore.Should().BeTrue();
        change.ShouldNotify.Should().BeFalse();
    }
}

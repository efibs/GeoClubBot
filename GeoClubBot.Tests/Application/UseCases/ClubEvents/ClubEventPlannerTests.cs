using FluentAssertions;
using UseCases.UseCases.ClubEvents;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubEvents;

public sealed class ClubEventPlannerTests
{
    private static readonly Guid ClubA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClubB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly ClubEventPlanOptions AllOn = new(UsePayloads: true, MemberEvents: true, MissionBoardEvents: true, ClubUpdatedEvents: true);

    private long _sequence;

    private PushOrder Next(DateTimeOffset? serverTime = null) => new(serverTime, ++_sequence);

    private ClubMemberJoinedPush Joined(Guid club, string user, DateTimeOffset? at = null) =>
        new(club, Next(at), user, $"nick-{user}", 0, T0);

    private ClubMemberLeftPush Left(Guid club, string user, DateTimeOffset? at = null) => new(club, Next(at), user);

    private static ClubEventPlan Plan(ClubEventPlanOptions options, params ClubPushEvent[] events) =>
        ClubEventPlanner.Plan(events, options);

    private static ClubEventPlan Plan(params ClubPushEvent[] events) => Plan(AllOn, events);

    [Fact]
    public void NoEvents_IsAnEmptyPlan()
    {
        Plan().IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AJoin_IsStoredFromThePayload()
    {
        var plan = Plan(Joined(ClubA, "u1"));

        plan.ResyncRosters.Should().BeFalse();
        plan.MemberChanges.Should().ContainSingle().Which.Should().Be(new MemberJoined("u1", ClubA, "nick-u1", 0, T0));
    }

    [Fact]
    public void ALeave_NamesTheClubLeft()
    {
        var plan = Plan(Left(ClubA, "u1"));

        var left = plan.MemberChanges.Should().ContainSingle().Which.Should().BeOfType<MemberLeft>().Subject;
        left.FromClubs.Should().BeEquivalentTo([ClubA]);
    }

    [Fact]
    public void ALeaveThenAJoinElsewhere_IsOneSwitch()
    {
        var plan = Plan(Left(ClubA, "u1"), Joined(ClubB, "u1"));

        plan.MemberChanges.Should().ContainSingle().Which.Should().BeOfType<MemberJoined>().Which.ClubId.Should().Be(ClubB);
    }

    [Fact]
    public void AJoinArrivingBeforeTheLeaveOfTheOldClub_IsStillOneSwitch()
    {
        // Two sockets: the join on B's arrived first, the leave on A's after it.
        var plan = Plan(Joined(ClubB, "u1"), Left(ClubA, "u1"));

        plan.MemberChanges.Should().ContainSingle().Which.Should().BeOfType<MemberJoined>().Which.ClubId.Should().Be(ClubB);
    }

    [Fact]
    public void GeoGuessrsTimestamps_OrderTheEvents_WhenEveryEventHasOne()
    {
        // Received join first, but GeoGuessr says the leave of the same club came after it.
        var plan = Plan(Joined(ClubA, "u1", T0.AddSeconds(1)), Left(ClubA, "u1", T0.AddSeconds(2)));
        plan.MemberChanges.Should().ContainSingle().Which.Should().BeOfType<MemberLeft>();

        var reversed = Plan(Left(ClubA, "u1", T0.AddSeconds(2)), Joined(ClubA, "u1", T0.AddSeconds(1)));
        reversed.MemberChanges.Should().ContainSingle().Which.Should().BeOfType<MemberLeft>();
    }

    [Fact]
    public void ArrivalOrder_DecidesWhenAnEventHasNoTimestamp()
    {
        var plan = Plan(Left(ClubA, "u1", T0.AddSeconds(5)), Joined(ClubA, "u1"));

        plan.MemberChanges.Should().ContainSingle().Which.Should().BeOfType<MemberJoined>();
    }

    [Fact]
    public void ASwitchFollowedByLeavingTheNewClub_LeavesBothClubs()
    {
        var plan = Plan(Left(ClubA, "u1"), Joined(ClubB, "u1"), Left(ClubB, "u1"));

        var left = plan.MemberChanges.Should().ContainSingle().Which.Should().BeOfType<MemberLeft>().Subject;
        left.FromClubs.Should().BeEquivalentTo([ClubA, ClubB]);
    }

    [Fact]
    public void LeavingAndRejoiningTheSameClub_EndsInTheClub()
    {
        var plan = Plan(Left(ClubA, "u1"), Joined(ClubA, "u1"));

        plan.MemberChanges.Should().ContainSingle().Which.Should().BeOfType<MemberJoined>().Which.ClubId.Should().Be(ClubA);
    }

    [Fact]
    public void EachUser_IsFoldedOnItsOwn()
    {
        var plan = Plan(Joined(ClubA, "u2"), Left(ClubA, "u1"), Joined(ClubB, "u3"));

        plan.MemberChanges.Select(c => c.UserId).Should().Equal("u1", "u2", "u3");
    }

    [Fact]
    public void ARefresh_ResyncsTheRosters_AndReReadsThatClubsBoardAndLevel_InsteadOfThePushedMembers()
    {
        var plan = Plan(Joined(ClubA, "u1"), new ClubRefreshRequested(ClubB, Next(), ClubRefreshReason.Connected));

        plan.ResyncRosters.Should().BeTrue();
        plan.MemberChanges.Should().BeEmpty();
        plan.BoardsToRecord.Should().BeEquivalentTo([ClubB]);
        plan.LevelChecks.Should().BeEquivalentTo([ClubB]);
    }

    [Fact]
    public void AMemberEventWithoutUsableData_ResyncsTheRosters()
    {
        var plan = Plan(Joined(ClubA, "u1"), new ClubRosterChangedPush(ClubA, Next()));

        plan.ResyncRosters.Should().BeTrue();
        plan.MemberChanges.Should().BeEmpty();
        plan.BoardsToRecord.Should().BeEmpty();
        plan.LevelChecks.Should().BeEmpty();
    }

    [Fact]
    public void WithPayloadsOff_AMemberEventResyncsTheRosters()
    {
        var plan = Plan(AllOn with { UsePayloads = false }, Joined(ClubA, "u1"));

        plan.ResyncRosters.Should().BeTrue();
        plan.MemberChanges.Should().BeEmpty();
    }

    [Fact]
    public void WithMemberEventsOff_MemberEventsAreIgnored_ButARefreshStillResyncs()
    {
        var options = AllOn with { MemberEvents = false };

        Plan(options, Joined(ClubA, "u1"), new ClubRosterChangedPush(ClubA, Next())).IsEmpty.Should().BeTrue();
        Plan(options, new ClubRefreshRequested(ClubA, Next(), ClubRefreshReason.Disconnected)).ResyncRosters.Should().BeTrue();
    }

    [Fact]
    public void ABoardUpdate_RecordsThatClubsClaims_UnlessSwitchedOff()
    {
        Plan(new ClubMissionBoardUpdatedPush(ClubA, Next())).BoardsToRecord.Should().BeEquivalentTo([ClubA]);
        Plan(AllOn with { MissionBoardEvents = false }, new ClubMissionBoardUpdatedPush(ClubA, Next())).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AClubUpdate_KeepsTheLatestLevelAndName()
    {
        var plan = Plan(
            new ClubUpdatedPush(ClubA, Next(T0.AddSeconds(2)), 7, "New"),
            new ClubUpdatedPush(ClubA, Next(T0.AddSeconds(1)), 6, "Old"));

        plan.LevelUpdates.Should().Equal(new Dictionary<Guid, int> { [ClubA] = 7 });
        plan.Renames.Should().Equal(new Dictionary<Guid, string> { [ClubA] = "New" });
    }

    [Fact]
    public void AClubUpdateWithoutLevelOrName_ChangesNothing()
    {
        Plan(new ClubUpdatedPush(ClubA, Next(), null, null)).IsEmpty.Should().BeTrue();
        Plan(new ClubUpdatedPush(ClubA, Next(), null, "  ")).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AClubUpdate_KeepsAnEarlierLevel_WhenALaterUpdateCarriesOnlyAName()
    {
        var plan = Plan(new ClubUpdatedPush(ClubA, Next(), 7, null), new ClubUpdatedPush(ClubA, Next(), null, "Renamed"));

        plan.LevelUpdates.Should().Equal(new Dictionary<Guid, int> { [ClubA] = 7 });
        plan.Renames.Should().Equal(new Dictionary<Guid, string> { [ClubA] = "Renamed" });
    }

    [Fact]
    public void AClubUpdate_IsIgnored_WhenSwitchedOff()
    {
        Plan(AllOn with { ClubUpdatedEvents = false }, new ClubUpdatedPush(ClubA, Next(), 7, "New")).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void APushedLevel_GivesWayToALevelCheckOfTheSameClub()
    {
        var plan = Plan(
            new ClubUpdatedPush(ClubA, Next(), 7, null),
            new ClubUpdatedPush(ClubB, Next(), 3, null),
            new ClubRefreshRequested(ClubA, Next(), ClubRefreshReason.Connected));

        plan.LevelChecks.Should().BeEquivalentTo([ClubA]);
        plan.LevelUpdates.Should().Equal(new Dictionary<Guid, int> { [ClubB] = 3 });
    }

    [Fact]
    public void ARosterResync_TakesOverThePushedNames()
    {
        var plan = Plan(new ClubUpdatedPush(ClubA, Next(), null, "New"), new ClubRosterChangedPush(ClubB, Next()));

        plan.Renames.Should().BeEmpty();
    }
}

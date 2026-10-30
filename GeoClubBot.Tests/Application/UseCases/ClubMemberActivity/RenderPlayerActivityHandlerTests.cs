using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using NSubstitute;
using UseCases.OutputPorts.Rendering;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class RenderPlayerActivityHandlerTests
{
    private const string Nickname = "Player1";
    private const string ClubName = "Main";
    private const int DefaultMinXp = 100;

    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Joined = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _clubMembers = Substitute.For<IClubMemberRepository>();
    private readonly IHistoryRepository _history = Substitute.For<IHistoryRepository>();
    private readonly IHistoryRenderer _renderer = Substitute.For<IHistoryRenderer>();
    private readonly MemoryStream _chart = new();
    private readonly GeoGuessrConfigurationBuilder _geoGuessr = new();

    private List<int>? _values;
    private List<DateTimeOffset>? _timestamps;
    private int? _target;

    public RenderPlayerActivityHandlerTests()
    {
        _renderer.RenderHistory(Arg.Any<List<int>>(), Arg.Any<List<DateTimeOffset>>(), Arg.Any<int>())
            .Returns(call =>
            {
                _values = call.ArgAt<List<int>>(0);
                _timestamps = call.ArgAt<List<DateTimeOffset>>(1);
                _target = call.ArgAt<int>(2);
                return _chart;
            });
        _clubs.ReadClubByNameAsync(ClubName, Arg.Any<CancellationToken>())
            .Returns(global::Entities.Club.Create(ClubId, ClubName, 1));
    }

    private Task<MemoryStream?> HandleAsync(int maxEntries = 10, string? clubName = null) =>
        new RenderPlayerActivityHandler(
                _clubs,
                _clubMembers,
                _history,
                _renderer,
                _geoGuessr.WithClub(ClubId, minXp: 40).BuildOptions(),
                new ActivityCheckerConfigurationBuilder().WithMinXp(DefaultMinXp).BuildOptions())
            .Handle(new RenderPlayerActivityQuery(Nickname, maxEntries, clubName), CancellationToken.None);

    private void Member(Guid? clubId) =>
        _clubMembers.ReadClubMemberByNicknameAsync(Nickname, Arg.Any<CancellationToken>())
            .Returns(new ClubMemberBuilder().WithNickname(Nickname).InClub(clubId).JoinedAt(Joined).Build());

    private static ClubMemberHistoryEntry Snapshot(int week, int xp) =>
        ClubMemberHistoryEntry.Create("user-1", ClubId, xp, Joined.AddDays(7 * week));

    private void History(params ClubMemberHistoryEntry[] entries) =>
        _history.ReadHistoryEntriesByPlayerNicknameAsync(Nickname, ClubId, Arg.Any<CancellationToken>())
            .Returns([.. entries]);

    [Fact]
    public async Task UnknownMember_WithoutAClubName_HasNoChart()
    {
        _clubMembers.ReadClubMemberByNicknameAsync(Nickname, Arg.Any<CancellationToken>()).Returns((ClubMember?)null);

        (await HandleAsync()).Should().BeNull();
    }

    [Fact]
    public async Task UnknownClubName_HasNoChart()
    {
        _clubs.ReadClubByNameAsync(ClubName, Arg.Any<CancellationToken>()).Returns((global::Entities.Club?)null);

        (await HandleAsync(clubName: ClubName)).Should().BeNull();
    }

    [Fact]
    public async Task MissingHistory_HasNoChart()
    {
        Member(ClubId);
        _history.ReadHistoryEntriesByPlayerNicknameAsync(Nickname, ClubId, Arg.Any<CancellationToken>())
            .Returns((List<ClubMemberHistoryEntry>?)null);

        (await HandleAsync()).Should().BeNull();
    }

    [Fact]
    public async Task TheJoinCountsAsAStartingPoint_SoOneSnapshotIsEnoughForABar()
    {
        Member(ClubId);
        History(Snapshot(1, 70));

        var chart = await HandleAsync();

        chart.Should().BeSameAs(_chart);
        _values.Should().Equal(70);
        _timestamps.Should().Equal(Joined, Joined.AddDays(7));
    }

    [Fact]
    public async Task ASingleSnapshotOfAnUntrackedPlayer_HasNothingToCompare()
    {
        _clubMembers.ReadClubMemberByNicknameAsync(Nickname, Arg.Any<CancellationToken>())
            .Returns(new ClubMemberBuilder().WithNickname(Nickname).InClub(ClubId).JoinedAt(Joined).Build(), (ClubMember?)null);
        History(Snapshot(1, 70));

        (await HandleAsync()).Should().BeNull();
    }

    [Fact]
    public async Task BarsAreTheGainsBetweenSnapshots_InTimeOrder()
    {
        Member(ClubId);
        History(Snapshot(2, 100), Snapshot(1, 40));

        await HandleAsync();

        _values.Should().Equal(40, 60);
    }

    [Fact]
    public async Task OnlyTheNewestEntriesAreShown()
    {
        Member(ClubId);
        History(Snapshot(1, 10), Snapshot(2, 30), Snapshot(3, 60));

        await HandleAsync(maxEntries: 2);

        _values.Should().Equal(20, 30);
        _timestamps.Should().Equal(Joined.AddDays(7), Joined.AddDays(14), Joined.AddDays(21));
    }

    [Fact]
    public async Task TargetIsTheMembersClubMinimum()
    {
        Member(ClubId);
        History(Snapshot(1, 70));

        await HandleAsync();

        _target.Should().Be(40);
    }

    [Fact]
    public async Task TargetFallsBackToTheDefault_ForAClubThatIsNoLongerConfigured()
    {
        var otherClub = Guid.NewGuid();
        Member(otherClub);
        _history.ReadHistoryEntriesByPlayerNicknameAsync(Nickname, otherClub, Arg.Any<CancellationToken>())
            .Returns([Snapshot(1, 70)]);

        await HandleAsync();

        _target.Should().Be(DefaultMinXp);
    }

    [Fact]
    public async Task FormerMember_CanStillBeLookedUpInTheirOldClub()
    {
        Member(clubId: null);
        History(Snapshot(1, 70), Snapshot(2, 100));

        var chart = await HandleAsync(clubName: ClubName);

        chart.Should().BeSameAs(_chart);
        _values.Should().Equal(70, 30);
        _target.Should().Be(DefaultMinXp);
    }

    [Fact]
    public async Task PlayerWithoutAMemberRecord_UsesTheDefaultTarget()
    {
        _clubMembers.ReadClubMemberByNicknameAsync(Nickname, Arg.Any<CancellationToken>()).Returns((ClubMember?)null);
        History(Snapshot(1, 10), Snapshot(2, 30));

        await HandleAsync(clubName: ClubName);

        _target.Should().Be(DefaultMinXp);
        _values.Should().Equal(20);
    }
}

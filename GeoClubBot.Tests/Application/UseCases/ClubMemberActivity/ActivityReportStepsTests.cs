using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Notifications;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

/// <summary>Who is named at the top and the bottom of the weekly report, and in what order.</summary>
public sealed class ActivityReportStepsTests
{
    private const string ClubName = "Main";
    private const int HistoryDepth = 4;

    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromDays(14);

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IActivityStatusMessageSender _messages = Substitute.For<IActivityStatusMessageSender>();
    private readonly IMissionClaimRepository _claims = Substitute.For<IMissionClaimRepository>();

    private List<ClubMemberAverageXp>? _top;
    private List<ClubMemberAverageXp>? _bottom;
    private List<ClubMemberMissionCompletion>? _slowest;
    private List<ClubMemberMissionCompletion>? _unfinished;

    public ActivityReportStepsTests()
    {
        _messages
            .When(m => m.SendAverageXpMessageAsync(Arg.Any<List<ClubMemberAverageXp>>(), Arg.Any<List<ClubMemberAverageXp>>(), ClubName, HistoryDepth, Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                _top = call.ArgAt<List<ClubMemberAverageXp>>(0);
                _bottom = call.ArgAt<List<ClubMemberAverageXp>>(1);
            });
        _messages
            .When(m => m.SendMissionCompletionMessageAsync(Arg.Any<List<ClubMemberMissionCompletion>>(), Arg.Any<List<ClubMemberMissionCompletion>>(), Window, Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                _slowest = call.ArgAt<List<ClubMemberMissionCompletion>>(0);
                _unfinished = call.ArgAt<List<ClubMemberMissionCompletion>>(1);
            });
    }

    // ---- Average XP -------------------------------------------------------

    private static ClubMemberAverageXp Average(string nickname, double xp, int joinedDaysAgo = 100) =>
        new(nickname, xp, Now.AddDays(-joinedDaysAgo));

    private Task RollUpAsync(int? topN, int? bottomN, params ClubMemberAverageXp[] averages)
    {
        _mediator.Send(new CalculateAverageXpQuery(ClubId, HistoryDepth), Arg.Any<CancellationToken>()).Returns(averages.ToList());
        return new ActivityAverageXpRollupStep(_mediator, _messages)
            .ExecuteAsync(ClubId, ClubName, topN, bottomN, HistoryDepth, CancellationToken.None);
    }

    [Fact]
    public async Task Rollup_NamesTheBestFirst_AndTheWorstFirst()
    {
        await RollUpAsync(1, 1, Average("Mid", 50), Average("Best", 90), Average("Worst", 10));

        _top!.Select(m => m.Nickname).Should().Equal("Best");
        _bottom!.Select(m => m.Nickname).Should().Equal("Worst");
    }

    [Fact]
    public async Task Rollup_BreaksTiesAtTheTop_ForTheLongestServing()
    {
        await RollUpAsync(1, null, Average("Newer", 90, 10), Average("Older", 90, 200));

        _top!.Select(m => m.Nickname).Should().Equal("Older");
    }

    [Fact]
    public async Task Rollup_BreaksTiesAtTheBottom_AgainstTheNewest()
    {
        await RollUpAsync(null, 1, Average("Older", 10, 200), Average("Newer", 10, 10));

        _bottom!.Select(m => m.Nickname).Should().Equal("Newer");
    }

    [Fact]
    public async Task Rollup_NeverNamesSomeoneOnBothLists()
    {
        await RollUpAsync(2, 2, Average("A", 90), Average("B", 50), Average("C", 10));

        _top!.Select(m => m.Nickname).Should().Equal("A", "B");
        _bottom!.Select(m => m.Nickname).Should().Equal("C");
    }

    [Fact]
    public async Task Rollup_LeavesOutTheListThatIsNotConfigured()
    {
        await RollUpAsync(null, 1, Average("A", 90), Average("B", 10));

        _top.Should().BeEmpty();
        _bottom.Should().ContainSingle();
    }

    [Fact]
    public async Task Rollup_ReportsTheBottom_WithoutATop()
    {
        await RollUpAsync(1, null, Average("A", 90), Average("B", 10));

        _top.Should().ContainSingle();
        _bottom.Should().BeEmpty();
    }

    [Fact]
    public async Task Rollup_WithNobodyToName_PostsNothing()
    {
        await RollUpAsync(3, 3);

        _messages.ReceivedCalls().Should().BeEmpty();
    }

    // ---- Mission completion -----------------------------------------------

    private static ClubMember Member(string userId) =>
        new ClubMemberBuilder().WithUserId(userId).WithNickname(userId.ToUpperInvariant()).Build();

    private Task ReportAsync(int slowestN, bool enabled = true, ClubMember[]? members = null, params MissionClaim[] claims)
    {
        _claims.ReadClaimsOfClubAsync(ClubId, Arg.Any<DateTimeOffset>(), Now, Arg.Any<CancellationToken>()).Returns([.. claims]);
        var config = new MissionClaimTrackingConfiguration { Schedule = "0 0 * ? * * *", Enabled = enabled, AverageWindow = Window };

        return new MissionCompletionReportStep(_claims, _messages, Options.Create(config))
            .ExecuteAsync(ClubId, members ?? [Member("a"), Member("b"), Member("c")], Now, slowestN, CancellationToken.None);
    }

    private static MissionClaim Took(string userId, double hours) =>
        MissionClaims.Completed(userId, Now.AddDays(-2), TimeSpan.FromHours(hours));

    /// <summary>Claimed in a board week that ended before the mission was finished.</summary>
    private static MissionClaim Abandoned(string userId, int daysAgo = 3) =>
        MissionClaims.Unfinished(userId, Now.AddDays(-daysAgo), Now.AddDays(-1));

    private static MissionClaim StillOpen(string userId, double hoursAgo) =>
        MissionClaims.Unfinished(userId, Now.AddHours(-hoursAgo), Now.AddDays(3));

    [Fact]
    public async Task Report_WhileTrackingIsOff_ReadsNothing()
    {
        await ReportAsync(3, enabled: false);

        _claims.ReceivedCalls().Should().BeEmpty();
        _messages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Report_ReadsFarEnoughBackForClaimsStillOpen()
    {
        await ReportAsync(3);

        await _claims.Received(1).ReadClaimsOfClubAsync(ClubId, MissionCompletionStats.ReadFrom(Now - Window), Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Report_WithoutClaims_PostsNothing()
    {
        await ReportAsync(3);

        _messages.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Report_ListsTheSlowestFirst_UpToTheLimit()
    {
        await ReportAsync(2, claims: [Took("a", 2), Took("b", 9), Took("c", 5)]);

        _slowest!.Select(c => c.Nickname).Should().Equal("B", "C");
    }

    [Fact]
    public async Task Report_BreaksTiesInTheSlowestByName_WhateverTheirCase()
    {
        var members = new[] { Member("b"), new ClubMemberBuilder().WithUserId("a").WithNickname("alice").Build() };

        await ReportAsync(2, members: members, claims: [Took("b", 5), Took("a", 5)]);

        _slowest!.Select(c => c.Nickname).Should().Equal("alice", "B");
    }

    [Fact]
    public async Task Report_LeavesSomeoneWhoFinishedNothing_OutOfTheSlowest()
    {
        await ReportAsync(3, claims: [Took("a", 2), Abandoned("b")]);

        _slowest!.Select(c => c.Nickname).Should().Equal("A");
    }

    [Fact]
    public async Task Report_IgnoresWhoeverLeftTheClub()
    {
        await ReportAsync(3, members: [Member("a")], claims: [Took("a", 2), Took("gone", 50)]);

        _slowest!.Select(c => c.Nickname).Should().Equal("A");
    }

    [Fact]
    public async Task Report_ListsWhoMostAbandoned_ThenWhoHoldsAClaimLongest_ThenByName()
    {
        await ReportAsync(
            3,
            claims: [StillOpen("c", 2), Abandoned("a"), Abandoned("a", 4), StillOpen("b", 20)]);

        _unfinished!.Select(c => c.Nickname).Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task Report_DoesNotCallAFinishedClaimUnfinished()
    {
        await ReportAsync(3, claims: [Took("a", 2)]);

        _unfinished.Should().BeEmpty();
    }

    [Fact]
    public async Task Report_PostsTheUnfinished_EvenWhenNobodyFinishedAnything()
    {
        await ReportAsync(3, claims: [Abandoned("a")]);

        _slowest.Should().BeEmpty();
        _unfinished!.Select(c => c.Nickname).Should().Equal("A");
    }

    [Fact]
    public async Task Report_WithAClaimOutsideThePeriod_HasNothingToSay()
    {
        await ReportAsync(3, claims: [MissionClaims.Completed("a", Now - Window - TimeSpan.FromDays(1), TimeSpan.FromHours(2))]);

        _messages.ReceivedCalls().Should().BeEmpty();
    }
}

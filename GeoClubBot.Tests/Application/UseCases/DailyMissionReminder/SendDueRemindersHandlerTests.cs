using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyMissionReminder;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.MissionBoards;
using DailyMissionReminderEntity = Entities.DailyMissionReminder;

namespace GeoClubBot.Tests.Application.UseCases.DailyMissionReminderTests;

public sealed class SendDueRemindersHandlerTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string UserId = "user-1";

    private readonly IDailyMissionReminderRepository _reminders = Substitute.For<IDailyMissionReminderRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IDiscordDirectMessageAccess _dm = Substitute.For<IDiscordDirectMessageAccess>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();
    private readonly IClubMissionBoardReader _boardReader = Substitute.For<IClubMissionBoardReader>();
    private readonly ILogger<SendDueRemindersHandler> _logger = Substitute.For<ILogger<SendDueRemindersHandler>>();

    // The claim cycle started 22 hours ago and resets in two.
    private readonly DateTimeOffset _nextReset = DateTimeOffset.UtcNow.AddHours(2);

    private bool _remindMissions = true;

    private SendDueRemindersHandler CreateHandler() => new(
        _reminders, _members, _dm, _mediator, _activityReader, ClubActivities.Classifier(), _boardReader,
        Options.Create(new DailyMissionReminderConfiguration
        {
            Schedule = "0 * * * * ?",
            DefaultMessage = "Don't forget to {{outstanding_text}}"
        }),
        Options.Create(new MissionBoardConfiguration()),
        new GeoGuessrConfigurationBuilder()
            .WithClubEntry(new GeoGuessrClubEntry { ClubId = ClubId, NcfaToken = "x", IsMain = true, RemindMissions = _remindMissions })
            .BuildOptions(),
        _logger);

    [Fact]
    public async Task Handle_DoesNothing_WhenNoRemindersDue()
    {
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<DailyMissionReminderEntity>());

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        await _dm.DidNotReceive().SendDirectMessageAsync(
            Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SkipsReminder_WhenTheStreakIsKeptAndAMissionWasClaimedThisCycle()
    {
        var reminder = ArrangeLinkedReminder(null);
        ArrangeToday(ClubActivities.Challenge(UserId));
        ArrangeBoard(Tile(claimedBy: UserId, claimedAt: DateTimeOffset.UtcNow.AddHours(-1), completed: true));

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        await _dm.DidNotReceive().SendDirectMessageAsync(
            Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        reminder.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_SkipsReminder_WhenTheStreakIsKeptAndNoMissionIsFree()
    {
        var reminder = ArrangeLinkedReminder(null);
        ArrangeToday(ClubActivities.Challenge(UserId));
        ArrangeBoard(Enumerable.Range(0, 9).Select(i => Tile(index: i, claimedBy: $"other{i}")).ToArray());

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        await _dm.DidNotReceive().SendDirectMessageAsync(
            Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        reminder.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_RemindsToClaim_WhenTheStreakIsKeptButAMissionIsStillFree()
    {
        ArrangeLinkedReminder(null);
        ArrangeToday(ClubActivities.Challenge(UserId));
        // Claimed in the previous cycle, so today's claim is still unused.
        var tiles = FreeTiles(9);
        tiles[0] = Tile(claimedBy: UserId, claimedAt: DateTimeOffset.UtcNow.AddHours(-30), completed: true);
        ArrangeBoard(tiles);

        var message = await CaptureSentMessageAsync();

        message.Should().Be(
            $"Don't forget to claim a club mission (8 still free on board 1, the daily claim resets <t:{_nextReset.ToUnixTimeSeconds()}:R>)!");
    }

    [Fact]
    public async Task Handle_RemindsOfBoth_WhenNothingIsDoneYet()
    {
        ArrangeLinkedReminder(null);
        ArrangeToday();
        ArrangeBoard(FreeTiles(9));

        var message = await CaptureSentMessageAsync();

        message.Should().StartWith("Don't forget to play the daily challenge (or a duel) and claim a club mission (9 still free");
    }

    [Fact]
    public async Task Handle_RemindsOnlyOfTheStreak_WhenAMissionWasClaimedThisCycle()
    {
        ArrangeLinkedReminder(null);
        ArrangeToday();
        var tiles = FreeTiles(9);
        tiles[0] = Tile(claimedBy: UserId, claimedAt: DateTimeOffset.UtcNow.AddHours(-1), completed: true);
        ArrangeBoard(tiles);

        var message = await CaptureSentMessageAsync();

        message.Should().Be("Don't forget to play the daily challenge (or a duel)!");
    }

    [Fact]
    public async Task Handle_RemindsToFinish_TheMissionTheMemberHolds()
    {
        ArrangeLinkedReminder(null);
        ArrangeToday(ClubActivities.Challenge(UserId));
        var tiles = FreeTiles(9);
        tiles[0] = Tile(claimedBy: UserId, claimedAt: DateTimeOffset.UtcNow.AddDays(-2), title: "Win 2 Ranked Duels", currentProgress: 1);
        ArrangeBoard(tiles);

        var message = await CaptureSentMessageAsync();

        message.Should().Be("Don't forget to finish your club mission **Win 2 Ranked Duels** (1/2) or request help!");
    }

    [Fact]
    public async Task Handle_LeavesMissionsOut_WhenTheBoardCannotBeRead()
    {
        var reminder = ArrangeLinkedReminder(null);
        ArrangeToday(ClubActivities.Challenge(UserId));
        _boardReader.ReadCurrentAsync(ClubId, Arg.Any<CancellationToken>()).Returns((ClubMissionBoardWeek?)null);

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        await _dm.DidNotReceive().SendDirectMessageAsync(
            Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        reminder.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_LeavesMissionsOut_WhenTheClubOptedOut()
    {
        _remindMissions = false;
        ArrangeLinkedReminder(null);
        ArrangeToday();
        ArrangeBoard(FreeTiles(9));

        var message = await CaptureSentMessageAsync();

        message.Should().Be("Don't forget to play the daily challenge (or a duel)!");
        await _boardReader.DidNotReceive().ReadCurrentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RemindsGenerically_WhenTheAccountIsNotLinked()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        ArrangeUnlinkedReminder(reminder);

        var message = await CaptureSentMessageAsync();

        message.Should().Be("Don't forget to play the daily challenge (or a duel) and claim a club mission if one is free!");
    }

    [Fact]
    public async Task Handle_UsesTheCustomMessage_WithThePlaceholderSubstituted()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, "Still to do: {{outstanding_text}}");
        ArrangeUnlinkedReminder(reminder);

        var message = await CaptureSentMessageAsync();

        message.Should().Be("Still to do: play the daily challenge (or a duel) and claim a club mission if one is free!");
    }

    [Fact]
    public async Task Handle_SubstitutesTheLegacyMissionTextPlaceholder_InRemindersStoredBeforeTheBoard()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, "Today you owe {{mission_text}}");
        ArrangeUnlinkedReminder(reminder);

        var message = await CaptureSentMessageAsync();

        message.Should().NotContain("{{mission_text}}").And.Contain("claim a club mission");
    }

    [Fact]
    public async Task Handle_DoesNotMarkSent_WhenDirectMessageFailsTransiently()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        ArrangeUnlinkedReminder(reminder);

        _dm.SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Unexpected("discord.dm.failed", "Transient failure.")));

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        reminder.LastSentDateUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MarksSent_WhenUserHasDmsDisabled_SoItIsNotRetried()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        ArrangeUnlinkedReminder(reminder);

        _dm.SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Forbidden("discord.dm.disabled", "DMs disabled.")));

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        // DMs-disabled is permanent for the day, so it is marked sent to avoid re-attempting.
        reminder.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_DeletesReminder_WhenUserHasLeftTheServer()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        ArrangeUnlinkedReminder(reminder);

        // Discord reports no mutual guild → the user has left the server and can never be DMed again.
        _dm.SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.NotFound(DiscordDmErrorCodes.NoMutualGuild, "No mutual guild.")));

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        _reminders.Received(1).DeleteReminder(reminder);
        reminder.LastSentDateUtc.Should().BeNull("a reminder for a departed user is removed, not marked sent");
    }

    private DailyMissionReminderEntity ArrangeLinkedReminder(string? customMessage)
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, customMessage);
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q!.DiscordUserId == 123UL),
            Arg.Any<CancellationToken>()).Returns(GeoGuessrUser.Create(UserId, "Player1", 123UL));

        var member = new ClubMemberBuilder()
            .WithUserId(UserId).WithDiscordUserId(123UL).InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(member);

        return reminder;
    }

    // The account lookup fails, so the handler can't see the member's activity and reminds of everything.
    private void ArrangeUnlinkedReminder(DailyMissionReminderEntity reminder)
    {
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q!.DiscordUserId == 123UL),
                Arg.Any<CancellationToken>())
            .Returns(Result<GeoGuessrUser>.Failure(Error.NotFound("account_linking.not_linked", "missing")));
    }

    private void ArrangeToday(params ReadClubActivitiesItemDto[] activities) =>
        _activityReader.ReadTodaysActivitiesAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(activities.ToList());

    private void ArrangeBoard(params ClubMissionTile[] tiles) =>
        _boardReader.ReadCurrentAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(Week(Board(1, tiles), nextClaimResetAt: _nextReset));

    private async Task<string?> CaptureSentMessageAsync()
    {
        string? captured = null;
        _dm.SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.ArgAt<string>(1);
                return Result.Success();
            });

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        return captured;
    }
}

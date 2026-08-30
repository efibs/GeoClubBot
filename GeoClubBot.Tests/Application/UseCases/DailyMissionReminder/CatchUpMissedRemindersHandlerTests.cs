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
using UseCases.OutputPorts.Rendering;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyMissionReminder;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;
using Xunit;
using DailyMissionReminderEntity = Entities.DailyMissionReminder;

namespace GeoClubBot.Tests.Application.UseCases.DailyMissionReminderTests;

/// <summary>
/// Tests the startup catch-up path of <see cref="SendDueRemindersHandler"/>: reminders whose time
/// passed while the bot was down are sent once, with at most one DM per user.
/// </summary>
public sealed class CatchUpMissedRemindersHandlerTests
{
    private static readonly Guid ClubId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IDailyMissionReminderRepository _reminders = Substitute.For<IDailyMissionReminderRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IDiscordDirectMessageAccess _dm = Substitute.For<IDiscordDirectMessageAccess>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();
    private readonly IDailyMissionRepository _dailyMissions = Substitute.For<IDailyMissionRepository>();
    private readonly IDailyMissionRenderer _renderer = Substitute.For<IDailyMissionRenderer>();
    private readonly ILogger<SendDueRemindersHandler> _logger = Substitute.For<ILogger<SendDueRemindersHandler>>();

    public CatchUpMissedRemindersHandlerTests()
    {
        // By default there are no stored missions, so the rendered mission text is empty.
        _dailyMissions.ReadLatestFetchedMissionsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<DailyMission>());

        _dm.SendDirectMessageAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    private SendDueRemindersHandler CreateHandler() => new(
        _reminders, _members, _dm, _mediator, _activityReader, ClubActivities.Classifier(),
        _dailyMissions, _renderer,
        Options.Create(new DailyMissionReminderConfiguration
        {
            Schedule = "0 * * * * ?",
            DefaultMessage = "Don't forget your daily missions!\n\n{{mission_text}}"
        }),
        _logger);

    private void ArrangeMissedReminders(params DailyMissionReminderEntity[] missed)
    {
        _reminders.ReadMissedRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(missed.ToList());
    }

    // The owner's account lookup fails, so the handler treats the mission as not yet completed
    // and proceeds to send.
    private void ArrangeUserNotCompleted(ulong discordUserId)
    {
        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q!.DiscordUserId == discordUserId),
                Arg.Any<CancellationToken>())
            .Returns(Result<GeoGuessrUser>.Failure(Error.NotFound("account_linking.not_linked", "missing")));
    }

    [Fact]
    public async Task Handle_DoesNothing_WhenNoRemindersWereMissed()
    {
        ArrangeMissedReminders();

        await CreateHandler().Handle(new CatchUpMissedRemindersCommand(), CancellationToken.None);

        await _dm.DidNotReceive().SendDirectMessageAsync(
            Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SendsMissedReminder_WhenItsTimePassedWhileBotWasDown()
    {
        // E.g. a 20:00 reminder, the bot was down 19:55–20:05: last sent yesterday, due time passed.
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(20, 0), null, "Custom!");
        reminder.MarkSent(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        ArrangeMissedReminders(reminder);
        ArrangeUserNotCompleted(123UL);

        await CreateHandler().Handle(new CatchUpMissedRemindersCommand(), CancellationToken.None);

        await _dm.Received(1).SendDirectMessageAsync(123UL, "Custom!", Arg.Any<CancellationToken>());
        reminder.LastSentDateUtc.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));

        // The catch-up must select via the missed-query, not the exact-minute due-query.
        await _reminders.DidNotReceive().ReadDueRemindersForUpdateAsync(
            Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SendsOnlyTheLatestMissedReminder_WhenAUserMissedSeveral()
    {
        var morning = DailyMissionReminderEntity.Create(123UL, new TimeOnly(9, 0), null, "Morning!");
        var noon = DailyMissionReminderEntity.Create(123UL, new TimeOnly(12, 0), null, "Noon!");
        ArrangeMissedReminders(morning, noon);
        ArrangeUserNotCompleted(123UL);

        await CreateHandler().Handle(new CatchUpMissedRemindersCommand(), CancellationToken.None);

        // One DM only — a restart must not burst several reminders at the same person.
        await _dm.Received(1).SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _dm.Received(1).SendDirectMessageAsync(123UL, "Noon!", Arg.Any<CancellationToken>());

        // Both are marked so another restart today cannot re-send either of them.
        morning.LastSentDateUtc.Should().NotBeNull();
        noon.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_SendsOneDmPerUser_WhenSeveralUsersMissedReminders()
    {
        var first = DailyMissionReminderEntity.Create(123UL, new TimeOnly(9, 0), null, null);
        var second = DailyMissionReminderEntity.Create(456UL, new TimeOnly(10, 0), null, null);
        ArrangeMissedReminders(first, second);
        ArrangeUserNotCompleted(123UL);
        ArrangeUserNotCompleted(456UL);

        await CreateHandler().Handle(new CatchUpMissedRemindersCommand(), CancellationToken.None);

        await _dm.Received(1).SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _dm.Received(1).SendDirectMessageAsync(456UL, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MarksAllMissedRemindersSent_WhenMissionAlreadyCompletedToday()
    {
        var morning = DailyMissionReminderEntity.Create(123UL, new TimeOnly(9, 0), null, null);
        var noon = DailyMissionReminderEntity.Create(123UL, new TimeOnly(12, 0), null, null);
        ArrangeMissedReminders(morning, noon);

        var linkedUser = GeoGuessrUser.Create("user-1", "Player1", 123UL);
        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q!.DiscordUserId == 123UL),
            Arg.Any<CancellationToken>()).Returns(linkedUser);

        var member = new ClubMemberBuilder()
            .WithUserId("user-1").WithDiscordUserId(123UL).InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(member);

        _activityReader.ReadTodaysActivitiesAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(new List<ReadClubActivitiesItemDto>
            {
                ClubActivities.Mission("user-1"), ClubActivities.Challenge("user-1")
            });

        await CreateHandler().Handle(new CatchUpMissedRemindersCommand(), CancellationToken.None);

        await _dm.DidNotReceive().SendDirectMessageAsync(
            Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        morning.LastSentDateUtc.Should().NotBeNull();
        noon.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_MarksAllMissedRemindersSent_WhenUserHasDmsDisabled()
    {
        var morning = DailyMissionReminderEntity.Create(123UL, new TimeOnly(9, 0), null, null);
        var noon = DailyMissionReminderEntity.Create(123UL, new TimeOnly(12, 0), null, null);
        ArrangeMissedReminders(morning, noon);
        ArrangeUserNotCompleted(123UL);

        _dm.SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Forbidden("discord.dm.disabled", "DMs disabled.")));

        await CreateHandler().Handle(new CatchUpMissedRemindersCommand(), CancellationToken.None);

        morning.LastSentDateUtc.Should().NotBeNull();
        noon.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_DeletesAllMissedReminders_WhenUserHasLeftTheServer()
    {
        var morning = DailyMissionReminderEntity.Create(123UL, new TimeOnly(9, 0), null, null);
        var noon = DailyMissionReminderEntity.Create(123UL, new TimeOnly(12, 0), null, null);
        ArrangeMissedReminders(morning, noon);
        ArrangeUserNotCompleted(123UL);

        _dm.SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.NotFound(DiscordDmErrorCodes.NoMutualGuild, "No mutual guild.")));

        await CreateHandler().Handle(new CatchUpMissedRemindersCommand(), CancellationToken.None);

        _reminders.Received(1).DeleteReminder(morning);
        _reminders.Received(1).DeleteReminder(noon);
        morning.LastSentDateUtc.Should().BeNull("a reminder for a departed user is removed, not marked sent");
        noon.LastSentDateUtc.Should().BeNull("a reminder for a departed user is removed, not marked sent");
    }

    [Fact]
    public async Task Handle_DoesNotMarkSent_WhenDmFailsTransiently()
    {
        var morning = DailyMissionReminderEntity.Create(123UL, new TimeOnly(9, 0), null, null);
        var noon = DailyMissionReminderEntity.Create(123UL, new TimeOnly(12, 0), null, null);
        ArrangeMissedReminders(morning, noon);
        ArrangeUserNotCompleted(123UL);

        _dm.SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Unexpected("discord.dm.failed", "Transient failure.")));

        await CreateHandler().Handle(new CatchUpMissedRemindersCommand(), CancellationToken.None);

        // Left unmarked so the next startup catch-up retries them.
        morning.LastSentDateUtc.Should().BeNull();
        noon.LastSentDateUtc.Should().BeNull();
    }
}

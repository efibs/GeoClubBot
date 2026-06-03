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

public sealed class SendDueRemindersHandlerTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const int DailyMissionXpReward = 20;

    private readonly IDailyMissionReminderRepository _reminders = Substitute.For<IDailyMissionReminderRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IDiscordDirectMessageAccess _dm = Substitute.For<IDiscordDirectMessageAccess>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();
    private readonly IDailyMissionRepository _dailyMissions = Substitute.For<IDailyMissionRepository>();
    private readonly IDailyMissionRenderer _renderer = Substitute.For<IDailyMissionRenderer>();
    private readonly ILogger<SendDueRemindersHandler> _logger = Substitute.For<ILogger<SendDueRemindersHandler>>();

    public SendDueRemindersHandlerTests()
    {
        // By default there are no stored missions, so the rendered mission text is empty.
        _dailyMissions.ReadLatestFetchedMissionsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<DailyMission>());
    }

    private SendDueRemindersHandler CreateHandler() => new(
        _reminders, _members, _dm, _mediator, _activityReader, _dailyMissions, _renderer,
        Options.Create(new DailyMissionReminderConfiguration
        {
            Schedule = "0 * * * * ?",
            DefaultMessage = "Don't forget your daily missions!\n\n{{mission_text}}",
            DailyMissionXpReward = DailyMissionXpReward
        }),
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
    public async Task Handle_SkipsAlreadyCompletedReminder_AndStillMarksItSent()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        var linkedUser = GeoGuessrUser.Create("user-1", "Player1", 123UL);
        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q.DiscordUserId == 123UL),
            Arg.Any<CancellationToken>()).Returns(linkedUser);

        var member = new ClubMemberBuilder()
            .WithUserId("user-1").WithDiscordUserId(123UL).InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(member);

        _activityReader.ReadTodaysActivitiesAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(new List<ReadClubActivitiesItemDto>
            {
                new() { UserId = "user-1", XpReward = DailyMissionXpReward, RecordedAt = DateTimeOffset.UtcNow }
            });

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        await _dm.DidNotReceive().SendDirectMessageAsync(
            Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        reminder.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_SendsDirectMessage_WhenMissionNotYetCompleted()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, "Custom!");
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        var linkedUser = GeoGuessrUser.Create("user-1", "Player1", 123UL);
        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q.DiscordUserId == 123UL),
            Arg.Any<CancellationToken>()).Returns(linkedUser);

        var member = new ClubMemberBuilder()
            .WithUserId("user-1").WithDiscordUserId(123UL).InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(member);

        // No matching XpReward in today's activities → mission not completed.
        _activityReader.ReadTodaysActivitiesAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(new List<ReadClubActivitiesItemDto>
            {
                new() { UserId = "user-1", XpReward = 99, RecordedAt = DateTimeOffset.UtcNow }
            });

        _dm.SendDirectMessageAsync(123UL, "Custom!", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        await _dm.Received(1).SendDirectMessageAsync(123UL, "Custom!", Arg.Any<CancellationToken>());
        reminder.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_DoesNotMarkSent_WhenDirectMessageFailsTransiently()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q.DiscordUserId == 123UL),
                Arg.Any<CancellationToken>())
            .Returns(Result<GeoGuessrUser>.Failure(Error.NotFound("account_linking.not_linked", "missing")));

        _dm.SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Unexpected("discord.dm.failed", "Transient failure.")));

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        reminder.LastSentDateUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MarksSent_WhenUserHasDmsDisabled_SoItIsNotRetried()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q.DiscordUserId == 123UL),
                Arg.Any<CancellationToken>())
            .Returns(Result<GeoGuessrUser>.Failure(Error.NotFound("account_linking.not_linked", "missing")));

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
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q.DiscordUserId == 123UL),
                Arg.Any<CancellationToken>())
            .Returns(Result<GeoGuessrUser>.Failure(Error.NotFound("account_linking.not_linked", "missing")));

        // Discord reports no mutual guild → the user has left the server and can never be DMed again.
        _dm.SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.NotFound(DiscordDmErrorCodes.NoMutualGuild, "No mutual guild.")));

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        _reminders.Received(1).DeleteReminder(reminder);
        reminder.LastSentDateUtc.Should().BeNull("a reminder for a departed user is removed, not marked sent");
    }

    [Fact]
    public async Task Handle_SubstitutesMissionText_IntoCustomMessage()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, "Today: {{mission_text}} - go!");
        ArrangeReminderThatWillBeSent(reminder);

        ArrangeMissions(
            ("PlayGames", "Play the Daily Challenge"),
            ("WinGames", "Win 5 Team Duels"));

        var captured = await CaptureSentMessageAsync();

        captured.Should().Be("Today: Play the Daily Challenge\nWin 5 Team Duels - go!");
    }

    [Fact]
    public async Task Handle_SubstitutesMissionText_IntoDefaultMessage()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        ArrangeReminderThatWillBeSent(reminder);

        ArrangeMissions(("PlayGames", "Play the Daily Challenge"));

        var captured = await CaptureSentMessageAsync();

        captured.Should().Be("Don't forget your daily missions!\n\nPlay the Daily Challenge");
    }

    [Fact]
    public async Task Handle_TrimsMissionPlaceholder_WhenNoMissionsStored()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        ArrangeReminderThatWillBeSent(reminder);

        // Repository default already returns no missions, so the placeholder renders to empty
        // and the trailing whitespace from the default message must be trimmed away.
        var captured = await CaptureSentMessageAsync();

        captured.Should().Be("Don't forget your daily missions!");
    }

    [Fact]
    public async Task Handle_DoesNotQueryMissions_WhenAllRemindersAlreadyDone()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        var linkedUser = GeoGuessrUser.Create("user-1", "Player1", 123UL);
        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q.DiscordUserId == 123UL),
            Arg.Any<CancellationToken>()).Returns(linkedUser);

        var member = new ClubMemberBuilder()
            .WithUserId("user-1").WithDiscordUserId(123UL).InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(member);

        _activityReader.ReadTodaysActivitiesAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(new List<ReadClubActivitiesItemDto>
            {
                new() { UserId = "user-1", XpReward = DailyMissionXpReward, RecordedAt = DateTimeOffset.UtcNow }
            });

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        await _dailyMissions.DidNotReceive().ReadLatestFetchedMissionsAsync(Arg.Any<CancellationToken>());
    }

    // Arranges a due reminder whose owner is treated as "not yet completed today" (the account
    // lookup fails), so the handler proceeds to build and send the message.
    private void ArrangeReminderThatWillBeSent(DailyMissionReminderEntity reminder)
    {
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q.DiscordUserId == 123UL),
                Arg.Any<CancellationToken>())
            .Returns(Result<GeoGuessrUser>.Failure(Error.NotFound("account_linking.not_linked", "missing")));
    }

    private void ArrangeMissions(params (string Type, string Rendered)[] missions)
    {
        var entities = missions
            .Select(m => DailyMission.Create(
                Guid.NewGuid(), m.Type, "Classic", 0, 1, false,
                DateTimeOffset.UtcNow, DailyMissionXpReward, "Xp", DateTimeOffset.UtcNow))
            .ToList();

        _dailyMissions.ReadLatestFetchedMissionsAsync(Arg.Any<CancellationToken>()).Returns(entities);

        foreach (var (type, rendered) in missions)
        {
            _renderer.RenderMission(Arg.Is<DailyMissionDto>(d => d.Type == type)).Returns(rendered);
        }
    }

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

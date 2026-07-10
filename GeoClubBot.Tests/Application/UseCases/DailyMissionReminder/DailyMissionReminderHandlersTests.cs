using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.DailyMissionReminder;
using Utilities;
using Xunit;
using DailyMissionReminderEntity = Entities.DailyMissionReminder;

namespace GeoClubBot.Tests.Application.UseCases.DailyMissionReminderTests;

public sealed class DailyMissionReminderHandlersTests
{
    private const ulong DiscordUserId = 123UL;
    private const string DefaultMessage = "Don't forget your daily missions!";
    private const int MaxReminders = 5;

    private readonly IDailyMissionReminderRepository _reminders = Substitute.For<IDailyMissionReminderRepository>();
    private readonly IDiscordDirectMessageAccess _dm = Substitute.For<IDiscordDirectMessageAccess>();
    private readonly ILogger<DailyMissionReminderHandlers> _logger = Substitute.For<ILogger<DailyMissionReminderHandlers>>();

    public DailyMissionReminderHandlersTests()
    {
        // Default: the user has no reminders yet.
        _reminders.ReadRemindersForUpdateAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(new List<DailyMissionReminderEntity>());
    }

    private DailyMissionReminderHandlers CreateHandler() => new(
        _reminders,
        _dm,
        Options.Create(new DailyMissionReminderConfiguration
        {
            Schedule = "0 * * * * ?",
            DefaultMessage = DefaultMessage,
            MaxRemindersPerUser = MaxReminders
        }),
        _logger);

    // ---- Add ----

    [Fact]
    public async Task Add_CreatesReminder_AndSendsConfirmationDm_ReturningAddedWhenDelivered()
    {
        _dm.SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await CreateHandler().Handle(
            new AddDailyMissionReminderCommand(DiscordUserId, new TimeOnly(8, 30), null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(AddReminderOutcome.Added);
        result.Value.DmDelivery.IsSuccess.Should().BeTrue();
        _reminders.Received(1).AddReminder(Arg.Any<DailyMissionReminderEntity>());
        await _dm.Received(1).SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_UpdatesExistingReminder_WhenOneExistsAtTheSameTime()
    {
        var existing = DailyMissionReminderEntity.Create(DiscordUserId, new TimeOnly(8, 30), null, "old");
        _reminders.ReadRemindersForUpdateAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(new List<DailyMissionReminderEntity> { existing });
        _dm.SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await CreateHandler().Handle(
            new AddDailyMissionReminderCommand(DiscordUserId, new TimeOnly(8, 30), null, "new"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(AddReminderOutcome.Updated);
        result.Value.ReminderId.Should().Be(existing.Id);
        existing.CustomMessage.Should().Be("new");
        _reminders.DidNotReceive().AddReminder(Arg.Any<DailyMissionReminderEntity>());
    }

    [Fact]
    public async Task Add_ReturnsConflict_WhenAtLimit_AndDoesNotSendDm()
    {
        var existing = Enumerable.Range(0, MaxReminders)
            .Select(i => DailyMissionReminderEntity.Create(DiscordUserId, new TimeOnly(8, i), null, null))
            .ToList();
        _reminders.ReadRemindersForUpdateAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateHandler().Handle(
            // A brand-new time (09:00) so it isn't treated as an update of an existing one.
            new AddDailyMissionReminderCommand(DiscordUserId, new TimeOnly(9, 0), null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        _reminders.DidNotReceive().AddReminder(Arg.Any<DailyMissionReminderEntity>());
        await _dm.DidNotReceive().SendDirectMessageAsync(
            Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_AtLimit_StillUpdatesReminderAtSameTime()
    {
        var existing = Enumerable.Range(0, MaxReminders)
            .Select(i => DailyMissionReminderEntity.Create(DiscordUserId, new TimeOnly(8, i), null, null))
            .ToList();
        _reminders.ReadRemindersForUpdateAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(existing);
        _dm.SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // 08:00 collides with an existing reminder → update, not a new insert, so the limit doesn't apply.
        var result = await CreateHandler().Handle(
            new AddDailyMissionReminderCommand(DiscordUserId, new TimeOnly(8, 0), null, "updated"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(AddReminderOutcome.Updated);
    }

    [Fact]
    public async Task Add_ReturnsForbiddenDmDelivery_WhenUserHasDmsDisabled_ButStillPersists()
    {
        _dm.SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Forbidden("discord.dm.disabled", "DMs disabled.")));

        var result = await CreateHandler().Handle(
            new AddDailyMissionReminderCommand(DiscordUserId, new TimeOnly(8, 30), null, null),
            CancellationToken.None);

        // The add itself succeeds; only the confirmation DM delivery reports the Forbidden error.
        result.IsSuccess.Should().BeTrue();
        result.Value.DmDelivery.IsFailure.Should().BeTrue();
        result.Value.DmDelivery.Error.Type.Should().Be(ErrorType.Forbidden);
        _reminders.Received(1).AddReminder(Arg.Any<DailyMissionReminderEntity>());
    }

    [Fact]
    public async Task Add_ConfirmationDm_ContainsScheduleAndCustomMessage()
    {
        _dm.SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await CreateHandler().Handle(
            new AddDailyMissionReminderCommand(DiscordUserId, new TimeOnly(9, 5), "Europe/Berlin", "wake up!"),
            CancellationToken.None);

        await _dm.Received(1).SendDirectMessageAsync(
            DiscordUserId,
            Arg.Is<string>(m => m.Contains("09:05") && m.Contains("Europe/Berlin") && m.Contains("wake up!")),
            Arg.Any<CancellationToken>());
    }

    // ---- Remove ----

    [Fact]
    public async Task Remove_DeletesReminder_WhenItExists()
    {
        var reminder = DailyMissionReminderEntity.Create(DiscordUserId, new TimeOnly(8, 30), null, null);
        _reminders.ReadReminderForUpdateAsync(reminder.Id, DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(reminder);

        var result = await CreateHandler().Handle(
            new RemoveDailyMissionReminderCommand(DiscordUserId, reminder.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _reminders.Received(1).DeleteReminder(reminder);
    }

    [Fact]
    public async Task Remove_ReturnsNotFound_WhenReminderDoesNotExist()
    {
        _reminders.ReadReminderForUpdateAsync(Arg.Any<Guid>(), DiscordUserId, Arg.Any<CancellationToken>())
            .Returns((DailyMissionReminderEntity?)null);

        var result = await CreateHandler().Handle(
            new RemoveDailyMissionReminderCommand(DiscordUserId, Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        _reminders.DidNotReceive().DeleteReminder(Arg.Any<DailyMissionReminderEntity>());
    }

    // ---- Clear ----

    [Fact]
    public async Task Clear_DeletesAllReminders_WhenAnyExist()
    {
        var first = DailyMissionReminderEntity.Create(DiscordUserId, new TimeOnly(8, 0), null, null);
        var second = DailyMissionReminderEntity.Create(DiscordUserId, new TimeOnly(20, 0), null, null);
        _reminders.ReadRemindersForUpdateAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(new List<DailyMissionReminderEntity> { first, second });

        var result = await CreateHandler().Handle(
            new ClearDailyMissionRemindersCommand(DiscordUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _reminders.Received(1).DeleteReminder(first);
        _reminders.Received(1).DeleteReminder(second);
    }

    [Fact]
    public async Task Clear_ReturnsNotFound_WhenNoneConfigured()
    {
        var result = await CreateHandler().Handle(
            new ClearDailyMissionRemindersCommand(DiscordUserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // ---- List ----

    [Fact]
    public async Task List_ReturnsReminders()
    {
        var reminder = DailyMissionReminderEntity.Create(DiscordUserId, new TimeOnly(8, 0), null, null);
        _reminders.ReadRemindersAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(new List<DailyMissionReminderEntity> { reminder });

        var result = await CreateHandler().Handle(
            new ListDailyMissionRemindersQuery(DiscordUserId), CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeSameAs(reminder);
    }
}

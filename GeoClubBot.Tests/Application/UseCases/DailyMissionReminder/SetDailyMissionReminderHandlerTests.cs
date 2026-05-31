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

public sealed class SetDailyMissionReminderHandlerTests
{
    private const ulong DiscordUserId = 123UL;
    private const string DefaultMessage = "Don't forget your daily missions!";

    private readonly IDailyMissionReminderRepository _reminders = Substitute.For<IDailyMissionReminderRepository>();
    private readonly IDiscordDirectMessageAccess _dm = Substitute.For<IDiscordDirectMessageAccess>();
    private readonly ILogger<DailyMissionReminderHandlers> _logger = Substitute.For<ILogger<DailyMissionReminderHandlers>>();

    private DailyMissionReminderHandlers CreateHandler() => new(
        _reminders,
        _dm,
        Options.Create(new DailyMissionReminderConfiguration
        {
            Schedule = "0 * * * * ?",
            DefaultMessage = DefaultMessage
        }),
        _logger);

    [Fact]
    public async Task Handle_CreatesReminder_AndSendsConfirmationDm_ReturningSuccessWhenDelivered()
    {
        _reminders.ReadReminderForUpdateAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns((DailyMissionReminderEntity?)null);
        _dm.SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await CreateHandler().Handle(
            new SetDailyMissionReminderCommand(DiscordUserId, new TimeOnly(8, 30), null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _reminders.Received(1).AddReminder(Arg.Any<DailyMissionReminderEntity>());
        await _dm.Received(1).SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsForbidden_WhenUserHasDmsDisabled()
    {
        _reminders.ReadReminderForUpdateAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns((DailyMissionReminderEntity?)null);
        _dm.SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Forbidden("discord.dm.disabled", "DMs disabled.")));

        var result = await CreateHandler().Handle(
            new SetDailyMissionReminderCommand(DiscordUserId, new TimeOnly(8, 30), null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_ReturnsUnexpected_OnTransientDmFailure()
    {
        _reminders.ReadReminderForUpdateAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns((DailyMissionReminderEntity?)null);
        _dm.SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Unexpected("discord.dm.failed", "Transient failure.")));

        var result = await CreateHandler().Handle(
            new SetDailyMissionReminderCommand(DiscordUserId, new TimeOnly(8, 30), null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unexpected);
    }

    [Fact]
    public async Task Handle_ConfirmationDm_ContainsScheduleAndCustomMessage()
    {
        _reminders.ReadReminderForUpdateAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns((DailyMissionReminderEntity?)null);
        _dm.SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await CreateHandler().Handle(
            new SetDailyMissionReminderCommand(DiscordUserId, new TimeOnly(9, 5), "Europe/Berlin", "wake up!"),
            CancellationToken.None);

        await _dm.Received(1).SendDirectMessageAsync(
            DiscordUserId,
            Arg.Is<string>(m => m.Contains("09:05") && m.Contains("Europe/Berlin") && m.Contains("wake up!")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConfirmationDm_FallsBackToDefaultMessage_WhenNoCustomMessage()
    {
        _reminders.ReadReminderForUpdateAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns((DailyMissionReminderEntity?)null);
        _dm.SendDirectMessageAsync(DiscordUserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await CreateHandler().Handle(
            new SetDailyMissionReminderCommand(DiscordUserId, new TimeOnly(8, 0), null, null),
            CancellationToken.None);

        await _dm.Received(1).SendDirectMessageAsync(
            DiscordUserId,
            Arg.Is<string>(m => m.Contains(DefaultMessage) && m.Contains("UTC")),
            Arg.Any<CancellationToken>());
    }
}

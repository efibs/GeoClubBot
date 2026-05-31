using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using Utilities;
using DomainDailyMissionReminder = Entities.DailyMissionReminder;

namespace UseCases.UseCases.DailyMissionReminder;

/// <summary>
/// Sets (or updates) the caller's daily mission reminder and sends a confirmation/test DM.
/// The reminder is always persisted; the returned <see cref="Result"/> reports the DM delivery:
/// success when delivered, a <see cref="ErrorType.Forbidden"/> error when the user has DMs from
/// the bot disabled/blocked (they must enable them), or a <see cref="ErrorType.Unexpected"/> error
/// for a transient failure (worth retrying). This lets the caller tell the two cases apart.
/// </summary>
public sealed record SetDailyMissionReminderCommand(
    ulong DiscordUserId,
    TimeOnly LocalTime,
    string? TimeZoneId,
    string? CustomMessage) : ICommand<Result>;

public sealed record StopDailyMissionReminderCommand(ulong DiscordUserId) : ICommand<Result>;

public sealed record GetDailyMissionReminderStatusQuery(ulong DiscordUserId) : IQuery<DomainDailyMissionReminder?>;

public sealed partial class DailyMissionReminderHandlers(
    IDailyMissionReminderRepository reminders,
    IDiscordDirectMessageAccess directMessageAccess,
    IOptions<DailyMissionReminderConfiguration> config,
    ILogger<DailyMissionReminderHandlers> logger)
    : IRequestHandler<SetDailyMissionReminderCommand, Result>,
      IRequestHandler<StopDailyMissionReminderCommand, Result>,
      IRequestHandler<GetDailyMissionReminderStatusQuery, DomainDailyMissionReminder?>
{
    public async Task<Result> Handle(SetDailyMissionReminderCommand request, CancellationToken cancellationToken)
    {
        var utcTime = ConvertToUtc(request.LocalTime, request.TimeZoneId);

        var existing = await reminders.ReadReminderForUpdateAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            existing.UpdateSchedule(utcTime, request.TimeZoneId, request.CustomMessage);
            LogReminderUpdated(request.DiscordUserId, utcTime);
        }
        else
        {
            var reminder = DomainDailyMissionReminder.Create(
                request.DiscordUserId, utcTime, request.TimeZoneId, request.CustomMessage);
            reminders.AddReminder(reminder);
            LogReminderCreated(request.DiscordUserId, utcTime);
        }

        var dmResult = await directMessageAccess
            .SendDirectMessageAsync(request.DiscordUserId, BuildConfirmationMessage(request), cancellationToken)
            .ConfigureAwait(false);

        if (dmResult.IsSuccess)
        {
            LogTestDmSent(request.DiscordUserId);
        }
        else
        {
            LogTestDmFailed(request.DiscordUserId, dmResult.Error.Code);
        }

        return dmResult;
    }

    private string BuildConfirmationMessage(SetDailyMissionReminderCommand request)
    {
        var tzDisplay = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "UTC" : request.TimeZoneId;
        var messageDisplay = string.IsNullOrWhiteSpace(request.CustomMessage)
            ? config.Value.DefaultMessage
            : request.CustomMessage;

        return
            "✅ Your daily mission reminder is now set up!\n" +
            $"Time: **{request.LocalTime:HH\\:mm}** ({tzDisplay})\n" +
            $"Message: {messageDisplay}\n\n" +
            "This is a confirmation message — you'll receive your reminder here each day at the scheduled "
            + "time, unless you've already completed your daily mission.";
    }

    public async Task<Result> Handle(StopDailyMissionReminderCommand request, CancellationToken cancellationToken)
    {
        var existing = await reminders.ReadReminderForUpdateAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            LogNoReminderFound(request.DiscordUserId);
            return Error.NotFound(
                "daily_mission_reminder.not_found",
                "No daily mission reminder is configured for this Discord user.");
        }

        reminders.DeleteReminder(existing);
        LogReminderStopped(request.DiscordUserId);
        return Result.Success();
    }

    public Task<DomainDailyMissionReminder?> Handle(GetDailyMissionReminderStatusQuery request, CancellationToken cancellationToken) =>
        reminders.ReadReminderAsync(request.DiscordUserId, cancellationToken);

    private static TimeOnly ConvertToUtc(TimeOnly localTime, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return localTime;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var localDateTime = today.ToDateTime(localTime);
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);

        return TimeOnly.FromDateTime(utcDateTime);
    }

    [LoggerMessage(LogLevel.Information, "Daily mission reminder updated for user {DiscordUserId} at {UtcTime} UTC.")]
    partial void LogReminderUpdated(ulong discordUserId, TimeOnly utcTime);

    [LoggerMessage(LogLevel.Information, "Daily mission reminder created for user {DiscordUserId} at {UtcTime} UTC.")]
    partial void LogReminderCreated(ulong discordUserId, TimeOnly utcTime);

    [LoggerMessage(LogLevel.Debug, "No daily mission reminder found for user {DiscordUserId}.")]
    partial void LogNoReminderFound(ulong discordUserId);

    [LoggerMessage(LogLevel.Information, "Daily mission reminder stopped for user {DiscordUserId}.")]
    partial void LogReminderStopped(ulong discordUserId);

    [LoggerMessage(LogLevel.Debug, "Confirmation DM sent to user {DiscordUserId} after setting daily mission reminder.")]
    partial void LogTestDmSent(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Failed to send confirmation DM to user {DiscordUserId} after setting daily mission reminder ({ErrorCode}).")]
    partial void LogTestDmFailed(ulong discordUserId, string errorCode);
}

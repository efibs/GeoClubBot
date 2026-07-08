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

/// <summary>Whether an <see cref="AddDailyMissionReminderCommand"/> created a new reminder or updated one at the same time.</summary>
public enum AddReminderOutcome
{
    Added,
    Updated
}

/// <summary>
/// Result of adding a reminder. The reminder is always persisted; <see cref="DmDelivery"/> reports
/// the confirmation/test DM delivery separately (success, an <see cref="ErrorType.Forbidden"/> error
/// when the user has DMs disabled, or an <see cref="ErrorType.Unexpected"/> transient failure) so the
/// caller can tell the user to enable DMs without treating a failed DM as a failed add.
/// </summary>
public sealed record AddReminderResult(Guid ReminderId, AddReminderOutcome Outcome, Result DmDelivery);

/// <summary>
/// Adds a new daily mission reminder for the caller (or updates the message of an existing reminder at
/// the same time) and sends a confirmation/test DM. Fails with <see cref="ErrorType.Conflict"/> when the
/// user already has the maximum number of reminders and is adding at a new time.
/// </summary>
public sealed record AddDailyMissionReminderCommand(
    ulong DiscordUserId,
    TimeOnly LocalTime,
    string? TimeZoneId,
    string? CustomMessage) : ICommand<Result<AddReminderResult>>;

/// <summary>Removes a single reminder owned by the caller.</summary>
public sealed record RemoveDailyMissionReminderCommand(ulong DiscordUserId, Guid ReminderId) : ICommand<Result>;

/// <summary>Removes all of the caller's reminders.</summary>
public sealed record ClearDailyMissionRemindersCommand(ulong DiscordUserId) : ICommand<Result>;

/// <summary>Lists all of the caller's reminders, ordered by time.</summary>
public sealed record ListDailyMissionRemindersQuery(ulong DiscordUserId) : IQuery<IReadOnlyList<DomainDailyMissionReminder>>;

public sealed partial class DailyMissionReminderHandlers(
    IDailyMissionReminderRepository reminders,
    IDiscordDirectMessageAccess directMessageAccess,
    IOptions<DailyMissionReminderConfiguration> config,
    ILogger<DailyMissionReminderHandlers> logger)
    : IRequestHandler<AddDailyMissionReminderCommand, Result<AddReminderResult>>,
      IRequestHandler<RemoveDailyMissionReminderCommand, Result>,
      IRequestHandler<ClearDailyMissionRemindersCommand, Result>,
      IRequestHandler<ListDailyMissionRemindersQuery, IReadOnlyList<DomainDailyMissionReminder>>
{
    public async Task<Result<AddReminderResult>> Handle(AddDailyMissionReminderCommand request, CancellationToken cancellationToken)
    {
        var utcTime = ConvertToUtc(request.LocalTime, request.TimeZoneId);

        var existing = await reminders.ReadRemindersForUpdateAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);

        // Adding at a time the user already has updates that reminder instead of creating a duplicate.
        var sameTime = existing.FirstOrDefault(r => r.ReminderTimeUtc == utcTime);

        DomainDailyMissionReminder reminder;
        AddReminderOutcome outcome;
        if (sameTime is not null)
        {
            sameTime.UpdateSchedule(utcTime, request.TimeZoneId, request.CustomMessage);
            reminder = sameTime;
            outcome = AddReminderOutcome.Updated;
            LogReminderUpdated(request.DiscordUserId, utcTime);
        }
        else
        {
            var maxReminders = config.Value.MaxRemindersPerUser;
            if (existing.Count >= maxReminders)
            {
                LogReminderLimitReached(request.DiscordUserId, maxReminders);
                return Error.Conflict(
                    "daily_mission_reminder.limit_reached",
                    $"You already have the maximum of {maxReminders} daily reminders. Remove one before adding another.");
            }

            reminder = DomainDailyMissionReminder.Create(
                request.DiscordUserId, utcTime, request.TimeZoneId, request.CustomMessage);
            reminders.AddReminder(reminder);
            outcome = AddReminderOutcome.Added;
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

        return new AddReminderResult(reminder.Id, outcome, dmResult);
    }

    private string BuildConfirmationMessage(AddDailyMissionReminderCommand request)
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

    public async Task<Result> Handle(RemoveDailyMissionReminderCommand request, CancellationToken cancellationToken)
    {
        var existing = await reminders
            .ReadReminderForUpdateAsync(request.ReminderId, request.DiscordUserId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            LogNoReminderFound(request.DiscordUserId);
            return Error.NotFound(
                "daily_mission_reminder.not_found",
                "That daily mission reminder does not exist.");
        }

        reminders.DeleteReminder(existing);
        LogReminderStopped(request.DiscordUserId);
        return Result.Success();
    }

    public async Task<Result> Handle(ClearDailyMissionRemindersCommand request, CancellationToken cancellationToken)
    {
        var existing = await reminders.ReadRemindersForUpdateAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);

        if (existing.Count == 0)
        {
            LogNoReminderFound(request.DiscordUserId);
            return Error.NotFound(
                "daily_mission_reminder.not_found",
                "No daily mission reminders are configured for this Discord user.");
        }

        foreach (var reminder in existing)
        {
            reminders.DeleteReminder(reminder);
        }

        LogRemindersCleared(request.DiscordUserId, existing.Count);
        return Result.Success();
    }

    public async Task<IReadOnlyList<DomainDailyMissionReminder>> Handle(ListDailyMissionRemindersQuery request, CancellationToken cancellationToken) =>
        await reminders.ReadRemindersAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);

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

    [LoggerMessage(LogLevel.Information, "User {DiscordUserId} tried to add a reminder but is already at the limit of {MaxReminders}.")]
    partial void LogReminderLimitReached(ulong discordUserId, int maxReminders);

    [LoggerMessage(LogLevel.Debug, "No daily mission reminder found for user {DiscordUserId}.")]
    partial void LogNoReminderFound(ulong discordUserId);

    [LoggerMessage(LogLevel.Information, "Daily mission reminder stopped for user {DiscordUserId}.")]
    partial void LogReminderStopped(ulong discordUserId);

    [LoggerMessage(LogLevel.Information, "Cleared {Count} daily mission reminders for user {DiscordUserId}.")]
    partial void LogRemindersCleared(ulong discordUserId, int count);

    [LoggerMessage(LogLevel.Debug, "Confirmation DM sent to user {DiscordUserId} after adding daily mission reminder.")]
    partial void LogTestDmSent(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Failed to send confirmation DM to user {DiscordUserId} after adding daily mission reminder ({ErrorCode}).")]
    partial void LogTestDmFailed(ulong discordUserId, string errorCode);
}

using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using DomainDailyMissionReminder = Entities.DailyMissionReminder;

namespace UseCases.UseCases.DailyMissionReminder;

public sealed record SetDailyMissionReminderCommand(
    ulong DiscordUserId,
    TimeOnly LocalTime,
    string? TimeZoneId,
    string? CustomMessage) : ICommand;

public sealed record StopDailyMissionReminderCommand(ulong DiscordUserId) : ICommand<bool>;

public sealed record GetDailyMissionReminderStatusQuery(ulong DiscordUserId) : IQuery<DomainDailyMissionReminder?>;

public sealed partial class DailyMissionReminderHandlers(
    IDailyMissionReminderRepository reminders,
    ILogger<DailyMissionReminderHandlers> logger)
    : IRequestHandler<SetDailyMissionReminderCommand, Unit>,
      IRequestHandler<StopDailyMissionReminderCommand, bool>,
      IRequestHandler<GetDailyMissionReminderStatusQuery, DomainDailyMissionReminder?>
{
    public async Task<Unit> Handle(SetDailyMissionReminderCommand request, CancellationToken cancellationToken)
    {
        var utcTime = ConvertToUtc(request.LocalTime, request.TimeZoneId);

        var existing = await reminders.ReadReminderForUpdateAsync(request.DiscordUserId).ConfigureAwait(false);

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

        return Unit.Value;
    }

    public async Task<bool> Handle(StopDailyMissionReminderCommand request, CancellationToken cancellationToken)
    {
        var existing = await reminders.ReadReminderForUpdateAsync(request.DiscordUserId).ConfigureAwait(false);

        if (existing is null)
        {
            LogNoReminderFound(request.DiscordUserId);
            return false;
        }

        reminders.DeleteReminder(existing);
        LogReminderStopped(request.DiscordUserId);
        return true;
    }

    public Task<DomainDailyMissionReminder?> Handle(GetDailyMissionReminderStatusQuery request, CancellationToken cancellationToken) =>
        reminders.ReadReminderAsync(request.DiscordUserId);

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
}

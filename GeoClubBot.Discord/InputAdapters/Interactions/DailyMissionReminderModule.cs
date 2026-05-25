using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.DailyMissionReminder;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("daily-reminder", "Commands for managing daily mission reminders")]
public class DailyMissionReminderModule(
    ISetDailyMissionReminderUseCase setDailyMissionReminderUseCase,
    IStopDailyMissionReminderUseCase stopDailyMissionReminderUseCase,
    IGetDailyMissionReminderStatusUseCase getDailyMissionReminderStatusUseCase,
    ISender mediator,
    ILogger<DailyMissionReminderModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("set", "Set a daily reminder to complete your GeoGuessr daily mission")]
    public Task SetReminderAsync(
        [Summary(description: "Time in HH:mm format (e.g. 09:00)")] string time,
        [Summary(description: "IANA timezone ID (e.g. Europe/Berlin). Defaults to UTC")] string? timezone = null,
        [Summary(description: "Custom reminder message")] string? message = null) =>
        ExecuteAsync(
            async _ =>
            {
                if (!TimeOnly.TryParseExact(time, "HH:mm", out var localTime))
                {
                    await FollowupAsync("Invalid time format. Please use HH:mm (e.g. 09:00).", ephemeral: true)
                        .ConfigureAwait(false);
                    return;
                }

                if (timezone != null)
                {
                    try
                    {
                        TimeZoneInfo.FindSystemTimeZoneById(timezone);
                    }
                    catch (TimeZoneNotFoundException)
                    {
                        await FollowupAsync(
                                $"Unknown timezone '{timezone}'. Please use an IANA timezone ID (e.g. Europe/Berlin, America/New_York).",
                                ephemeral: true)
                            .ConfigureAwait(false);
                        return;
                    }
                }

                await setDailyMissionReminderUseCase
                    .SetReminderAsync(Context.User.Id, localTime, timezone, message)
                    .ConfigureAwait(false);

                var tzDisplay = timezone ?? "UTC";
                await FollowupAsync($"Daily reminder set for **{time}** ({tzDisplay}). You will receive a DM each day at that time.",
                        ephemeral: true)
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to set the daily reminder. Please try again later.");

    [SlashCommand("stop", "Stop your daily mission reminder")]
    public Task StopReminderAsync() =>
        ExecuteAsync(
            async _ =>
            {
                var stopped = await stopDailyMissionReminderUseCase
                    .StopReminderAsync(Context.User.Id)
                    .ConfigureAwait(false);

                await FollowupAsync(
                        stopped
                            ? "Your daily mission reminder has been stopped."
                            : "You don't have an active daily mission reminder.",
                        ephemeral: true)
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to stop the daily reminder. Please try again later.");

    [SlashCommand("status", "Check the status of your daily mission reminder")]
    public Task StatusAsync() =>
        ExecuteAsync(
            async _ =>
            {
                var reminder = await getDailyMissionReminderStatusUseCase
                    .GetStatusAsync(Context.User.Id)
                    .ConfigureAwait(false);

                if (reminder == null)
                {
                    await FollowupAsync("You don't have an active daily mission reminder.", ephemeral: true)
                        .ConfigureAwait(false);
                    return;
                }

                // Convert UTC time back to local for display
                var displayTime = _convertToLocal(reminder.ReminderTimeUtc, reminder.TimeZoneId);
                var tzDisplay = reminder.TimeZoneId ?? "UTC";
                var messageDisplay = string.IsNullOrWhiteSpace(reminder.CustomMessage) ? "Default" : reminder.CustomMessage;
                var lastSentDisplay = reminder.LastSentDateUtc?.ToString("yyyy-MM-dd") ?? "Never";

                await FollowupAsync(
                        $"**Daily Mission Reminder**\n" +
                        $"Time: **{displayTime:HH\\:mm}** ({tzDisplay})\n" +
                        $"Message: {messageDisplay}\n" +
                        $"Last sent: {lastSentDisplay}",
                        ephemeral: true)
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to check the daily reminder status. Please try again later.");

    private static TimeOnly _convertToLocal(TimeOnly utcTime, string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return utcTime;
        }

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var utcDateTime = today.ToDateTime(utcTime, DateTimeKind.Utc);
            var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, tz);
            return TimeOnly.FromDateTime(localDateTime);
        }
        catch
        {
            return utcTime;
        }
    }
}

using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.DailyMissionReminder;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("daily-reminder", "Commands for managing daily mission reminders")]
public partial class DailyMissionReminderModule(
    ISetDailyMissionReminderUseCase setDailyMissionReminderUseCase,
    IStopDailyMissionReminderUseCase stopDailyMissionReminderUseCase,
    IGetDailyMissionReminderStatusUseCase getDailyMissionReminderStatusUseCase,
    ILogger<DailyMissionReminderModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("set", "Set a daily reminder to complete your GeoGuessr daily mission")]
    public async Task SetReminderAsync(
        [Summary(description: "Time in HH:mm format (e.g. 09:00)")] string time,
        [Summary(description: "IANA timezone ID (e.g. Europe/Berlin). Defaults to UTC")] string? timezone = null,
        [Summary(description: "Custom reminder message")] string? message = null)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            // Parse the time
            if (!TimeOnly.TryParseExact(time, "HH:mm", out var localTime))
            {
                await FollowupAsync("Invalid time format. Please use HH:mm (e.g. 09:00).", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            // Validate timezone if provided
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

            // Set the reminder
            await setDailyMissionReminderUseCase
                .SetReminderAsync(Context.User.Id, localTime, timezone, message)
                .ConfigureAwait(false);

            // Build response
            var tzDisplay = timezone ?? "UTC";
            await FollowupAsync($"Daily reminder set for **{time}** ({tzDisplay}). You will receive a DM each day at that time.",
                    ephemeral: true)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSetReminderFailed(ex, Context.User.Id);
            await FollowupAsync("Failed to set the daily reminder. Please try again later.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    [SlashCommand("stop", "Stop your daily mission reminder")]
    public async Task StopReminderAsync()
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            // Stop the reminder
            var stopped = await stopDailyMissionReminderUseCase
                .StopReminderAsync(Context.User.Id)
                .ConfigureAwait(false);

            if (stopped)
            {
                await FollowupAsync("Your daily mission reminder has been stopped.", ephemeral: true)
                    .ConfigureAwait(false);
            }
            else
            {
                await FollowupAsync("You don't have an active daily mission reminder.", ephemeral: true)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogStopReminderFailed(ex, Context.User.Id);
            await FollowupAsync("Failed to stop the daily reminder. Please try again later.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    [SlashCommand("status", "Check the status of your daily mission reminder")]
    public async Task StatusAsync()
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            // Get the status
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
        }
        catch (Exception ex)
        {
            LogStatusCheckFailed(ex, Context.User.Id);
            await FollowupAsync("Failed to check the daily reminder status. Please try again later.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

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

    [LoggerMessage(LogLevel.Error, "Failed to set daily mission reminder for user {DiscordUserId}.")]
    partial void LogSetReminderFailed(Exception ex, ulong discordUserId);

    [LoggerMessage(LogLevel.Error, "Failed to stop daily mission reminder for user {DiscordUserId}.")]
    partial void LogStopReminderFailed(Exception ex, ulong discordUserId);

    [LoggerMessage(LogLevel.Error, "Failed to check daily mission reminder status for user {DiscordUserId}.")]
    partial void LogStatusCheckFailed(Exception ex, ulong discordUserId);
}

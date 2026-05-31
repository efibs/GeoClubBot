using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.DailyMissionReminder;
using Utilities;

namespace GeoClubBot.Discord.InputAdapters.Interactions.DailyMissionReminder;

[CommandContextType(InteractionContextType.Guild)]
[Group("daily-reminder", "Commands for managing daily mission reminders")]
public class DailyMissionReminderModule(
    ISender mediator,
    ILogger<DailyMissionReminderModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("set", "Set a daily reminder to complete your GeoGuessr daily mission")]
    public Task SetReminderAsync(
        [Summary(description: "Time in HH:mm format (e.g. 09:00)")] string time,
        [Summary(description: "IANA timezone ID (e.g. Europe/Berlin). Defaults to UTC")] string? timezone = null,
        [Summary(description: "Custom reminder message")] string? message = null) =>
        ExecuteAsync(
            async ct =>
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

                var dmResult = await Mediator
                    .Send(new SetDailyMissionReminderCommand(Context.User.Id, localTime, timezone, message), ct)
                    .ConfigureAwait(false);

                var tzDisplay = timezone ?? "UTC";
                var baseMessage = $"Daily reminder set for **{time}** ({tzDisplay}). You will receive a DM each day at that time.";

                string followup;
                if (dmResult.IsSuccess)
                {
                    followup = $"{baseMessage}\n\n📬 I've just sent you a test DM. If you **didn't** receive it, you must enable "
                        + "direct messages from server members/bots, otherwise you won't get your reminders.";
                }
                else if (dmResult.Error.Type == ErrorType.Forbidden)
                {
                    // Permanent: the user has DMs from the bot disabled or has blocked it.
                    followup = $"{baseMessage}\n\n⚠️ I couldn't send you a test DM because you don't accept direct messages "
                        + "from the bot, so you won't receive your reminders. Please enable direct messages from server "
                        + "members/bots (Server name → Privacy Settings → Allow direct messages), then run this command again.";
                }
                else
                {
                    // Transient: your reminder is saved, the test DM just failed this time.
                    followup = $"{baseMessage}\n\n⚠️ Your reminder is saved, but I couldn't send a test DM right now due to a "
                        + "temporary problem. If you don't receive reminders, make sure you allow direct messages from "
                        + "server members/bots and re-run this command to test again.";
                }

                await FollowupAsync(followup, ephemeral: true).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to set the daily reminder. Please try again later.");

    [SlashCommand("stop", "Stop your daily mission reminder")]
    public Task StopReminderAsync() =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new StopDailyMissionReminderCommand(Context.User.Id), ct)
                    .ConfigureAwait(false);

                await FollowupAsync(
                        result.IsSuccess
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
            async ct =>
            {
                var reminder = await Mediator
                    .Send(new GetDailyMissionReminderStatusQuery(Context.User.Id), ct)
                    .ConfigureAwait(false);

                if (reminder == null)
                {
                    await FollowupAsync("You don't have an active daily mission reminder.", ephemeral: true)
                        .ConfigureAwait(false);
                    return;
                }

                // Convert UTC time back to local for display
                var displayTime = ConvertToLocal(reminder.ReminderTimeUtc, reminder.TimeZoneId);
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

    private static TimeOnly ConvertToLocal(TimeOnly utcTime, string? timeZoneId)
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

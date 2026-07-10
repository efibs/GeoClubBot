using System.Text;
using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
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
    [SlashCommand("add", "Add a daily reminder to complete your GeoGuessr daily mission")]
    public Task AddReminderAsync(
        [Summary(description: "Time in HH:mm format (e.g. 09:00)")] string time,
        [Autocomplete(typeof(TimezoneAutocompleteHandler))][Summary(description: "IANA timezone ID (e.g. Europe/Berlin). Defaults to UTC")] string? timezone = null,
        [Summary(description: "Custom reminder message. Use {{mission_text}} to insert today's mission.")] string? message = null) =>
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

                var result = await Mediator
                    .Send(new AddDailyMissionReminderCommand(Context.User.Id, localTime, timezone, message), ct)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    // Currently only the per-user limit (Conflict); surface its message.
                    await FollowupAsync(FriendlyMessageFor(result.Error), ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var tzDisplay = timezone ?? "UTC";
                var verb = result.Value.Outcome == AddReminderOutcome.Updated ? "updated" : "set";
                var baseMessage = $"Daily reminder {verb} for **{time}** ({tzDisplay}). You will receive a DM each day at that time.";

                var dmResult = result.Value.DmDelivery;
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
            failureMessage: "Failed to add the daily reminder. Please try again later.");

    [SlashCommand("remove", "Remove one of your daily mission reminders")]
    public Task RemoveReminderAsync(
        [Autocomplete(typeof(ReminderAutocompleteHandler))][Summary(description: "The reminder to remove")] string reminder) =>
        ExecuteAsync(
            async ct =>
            {
                if (!Guid.TryParse(reminder, out var reminderId))
                {
                    await FollowupAsync("Please pick a reminder from the list.", ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var result = await Mediator
                    .Send(new RemoveDailyMissionReminderCommand(Context.User.Id, reminderId), ct)
                    .ConfigureAwait(false);

                await FollowupAsync(
                        result.IsSuccess
                            ? "That daily mission reminder has been removed."
                            : "That daily mission reminder could not be found.",
                        ephemeral: true)
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to remove the daily reminder. Please try again later.");

    [SlashCommand("clear", "Remove all of your daily mission reminders")]
    public Task ClearRemindersAsync() =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new ClearDailyMissionRemindersCommand(Context.User.Id), ct)
                    .ConfigureAwait(false);

                await FollowupAsync(
                        result.IsSuccess
                            ? "All of your daily mission reminders have been removed."
                            : "You don't have any daily mission reminders.",
                        ephemeral: true)
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to clear the daily reminders. Please try again later.");

    [SlashCommand("list", "List your daily mission reminders")]
    public Task ListAsync() =>
        ExecuteAsync(
            async ct =>
            {
                var reminders = await Mediator
                    .Send(new ListDailyMissionRemindersQuery(Context.User.Id), ct)
                    .ConfigureAwait(false);

                if (reminders.Count == 0)
                {
                    await FollowupAsync("You don't have any daily mission reminders.", ephemeral: true)
                        .ConfigureAwait(false);
                    return;
                }

                var builder = new StringBuilder();
                builder.AppendLine($"**Your Daily Mission Reminders** ({reminders.Count})");

                foreach (var reminder in reminders)
                {
                    var displayTime = ConvertToLocal(reminder.ReminderTimeUtc, reminder.TimeZoneId);
                    var tzDisplay = reminder.TimeZoneId ?? "UTC";
                    var messageDisplay = string.IsNullOrWhiteSpace(reminder.CustomMessage) ? "Default" : reminder.CustomMessage;
                    var lastSentDisplay = reminder.LastSentDateUtc?.ToString("yyyy-MM-dd") ?? "Never";

                    builder.AppendLine();
                    builder.AppendLine($"• **{displayTime:HH\\:mm}** ({tzDisplay})");
                    builder.AppendLine($"  Message: {messageDisplay}");
                    builder.AppendLine($"  Last sent: {lastSentDisplay}");
                }

                await FollowupAsync(builder.ToString(), ephemeral: true).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to list your daily reminders. Please try again later.");

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

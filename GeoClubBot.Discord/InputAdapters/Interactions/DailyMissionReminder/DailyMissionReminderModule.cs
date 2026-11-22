using System.Text;
using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.DailyMissionReminder;
using Utilities;

namespace GeoClubBot.Discord.InputAdapters.Interactions.DailyMissionReminder;

[CommandContextType(InteractionContextType.Guild)]
[HelpFeature("reminders", Emoji = "⏰", Title = "Daily reminder", Order = 20,
    Summary = "A DM at times you choose, naming what you still owe today: your streak, a club mission.")]
[HelpSection(1, "What it reminds you of", """
    - **Your streak:** play the daily challenge or a duel (UTC day).
    - **A club mission:** claim one while the board has a free mission and you haven't used today's claim. If you still hold a mission from an earlier day, it reminds you to finish it (or ask for help) instead.
    The DM names only what is left, and none is sent once nothing is. Claims reset at 12:00 UK time; the reminder says when. Without a linked account I can't see your activity, so you are reminded of both.
    You can have several reminders, each with its own time and message. If I was offline at a reminder's time, I send one catch-up DM when I'm back.
    """)]
[Group("daily-reminder", "Your daily DMs to keep your streak and claim a club mission")]
public class DailyMissionReminderModule(
    ISender mediator,
    ILogger<DailyMissionReminderModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("add", "Add a daily reminder, or change the one at that time")]
    [HelpDetail("""
        Write `{{outstanding_text}}` in your message where what you still owe should go. It starts with a verb and ends the sentence, so `Time for GeoGuessr! Don't forget to {{outstanding_text}}` becomes
        > Time for GeoGuessr! Don't forget to play the daily challenge (or a duel) and claim a club mission (5 still free on board 3, the daily claim resets in 4 hours)!
        Without it, only your own text is sent.
        """)]
    public Task AddReminderAsync(
        [Summary(description: "When, in 24-hour HH:mm, e.g. 09:00 or 21:30")] string time,
        [Autocomplete(typeof(TimezoneAutocompleteHandler))][Summary(description: "Your time zone, e.g. Europe/Berlin; UTC if left out")] string? timezone = null,
        [Summary(description: "Your own text; {{outstanding_text}} marks where what you still owe goes")] string? message = null) =>
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

    [SlashCommand("remove", "Remove one of your daily reminders")]
    public Task RemoveReminderAsync(
        [Autocomplete(typeof(ReminderAutocompleteHandler))][Summary(description: "The reminder to remove, picked by its time")] string reminder) =>
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
                            ? "That daily reminder has been removed."
                            : "That daily reminder could not be found.",
                        ephemeral: true)
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to remove the daily reminder. Please try again later.");

    [SlashCommand("clear", "Remove all of your daily reminders")]
    public Task ClearRemindersAsync() =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new ClearDailyMissionRemindersCommand(Context.User.Id), ct)
                    .ConfigureAwait(false);

                await FollowupAsync(
                        result.IsSuccess
                            ? "All of your daily reminders have been removed."
                            : "You don't have any daily reminders.",
                        ephemeral: true)
                    .ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Failed to clear the daily reminders. Please try again later.");

    [SlashCommand("list", "List your daily reminders: times, time zones, messages and when each was last sent")]
    public Task ListAsync() =>
        ExecuteAsync(
            async ct =>
            {
                var reminders = await Mediator
                    .Send(new ListDailyMissionRemindersQuery(Context.User.Id), ct)
                    .ConfigureAwait(false);

                if (reminders.Count == 0)
                {
                    await FollowupAsync("You don't have any daily reminders.", ephemeral: true)
                        .ConfigureAwait(false);
                    return;
                }

                var builder = new StringBuilder();
                builder.AppendLine($"**Your Daily Reminders** ({reminders.Count})");

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

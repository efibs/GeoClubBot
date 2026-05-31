using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;

namespace UseCases.UseCases.DailyMissionReminder;

public sealed record SendDueRemindersCommand : ICommand;

public sealed partial class SendDueRemindersHandler(
    IDailyMissionReminderRepository reminders,
    IClubMemberRepository members,
    IDiscordDirectMessageAccess directMessageAccess,
    ISender mediator,
    IGeoGuessrActivityReader activityReader,
    IOptions<DailyMissionReminderConfiguration> config,
    ILogger<SendDueRemindersHandler> logger) : IRequestHandler<SendDueRemindersCommand, Unit>
{
    public async Task<Unit> Handle(SendDueRemindersCommand request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var currentTime = new TimeOnly(now.Hour, now.Minute);
        var today = DateOnly.FromDateTime(now);

        var dueReminders = await reminders
            .ReadDueRemindersForUpdateAsync(currentTime, today, cancellationToken)
            .ConfigureAwait(false);

        if (dueReminders.Count == 0)
        {
            return Unit.Value;
        }

        LogSendingReminders(dueReminders.Count);

        var defaultMessage = config.Value.DefaultMessage;
        var dailyMissionXpReward = config.Value.DailyMissionXpReward;

        foreach (var reminder in dueReminders)
        {
            var alreadyDone = await HasUserCompletedDailyMissionTodayAsync(reminder.DiscordUserId, dailyMissionXpReward, cancellationToken)
                .ConfigureAwait(false);

            if (alreadyDone)
            {
                reminder.MarkSent(today);
                LogReminderSkippedAlreadyDone(reminder.DiscordUserId);
                continue;
            }

            var message = string.IsNullOrWhiteSpace(reminder.CustomMessage)
                ? defaultMessage
                : reminder.CustomMessage;

            var dmResult = await directMessageAccess
                .SendDirectMessageAsync(reminder.DiscordUserId, message, cancellationToken)
                .ConfigureAwait(false);

            if (dmResult.IsSuccess)
            {
                reminder.MarkSent(today);
                LogReminderSent(reminder.DiscordUserId);
            }
            else if (dmResult.Error.Type == ErrorType.Forbidden)
            {
                // The user has DMs from the bot disabled/blocked — retrying won't help until they fix it,
                // so mark it sent to stop this run (and the rest of the due window) from hammering Discord.
                reminder.MarkSent(today);
                LogReminderDmsDisabled(reminder.DiscordUserId);
            }
            else
            {
                // Transient failure — left unmarked so a later run can retry.
                LogReminderFailed(reminder.DiscordUserId);
            }
        }

        return Unit.Value;
    }

    private async Task<bool> HasUserCompletedDailyMissionTodayAsync(ulong discordUserId, int dailyMissionXpReward, CancellationToken cancellationToken)
    {
        var linkedUser = await mediator
            .Send(new GetLinkedGeoGuessrUserQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);

        if (linkedUser.IsFailure)
        {
            return false;
        }

        var clubMember = await members.ReadClubMemberByUserIdAsync(linkedUser.Value.UserId, cancellationToken).ConfigureAwait(false);

        if (clubMember?.ClubId is null)
        {
            return false;
        }

        var todaysActivities = await activityReader
            .ReadTodaysActivitiesAsync(clubMember.ClubId.Value, cancellationToken)
            .ConfigureAwait(false);

        return todaysActivities.Any(a => a.UserId == linkedUser.Value.UserId && a.XpReward == dailyMissionXpReward);
    }

    [LoggerMessage(LogLevel.Information, "Sending {Count} daily mission reminders.")]
    partial void LogSendingReminders(int count);

    [LoggerMessage(LogLevel.Debug, "Daily mission reminder sent to user {DiscordUserId}.")]
    partial void LogReminderSent(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Failed to send daily mission reminder to user {DiscordUserId}.")]
    partial void LogReminderFailed(ulong discordUserId);

    [LoggerMessage(LogLevel.Error, "Could not deliver daily mission reminder to user {DiscordUserId} - they have DMs from the bot disabled or blocked the bot; not retrying today.")]
    partial void LogReminderDmsDisabled(ulong discordUserId);

    [LoggerMessage(LogLevel.Debug, "Daily mission reminder skipped for user {DiscordUserId} - already completed today.")]
    partial void LogReminderSkippedAlreadyDone(ulong discordUserId);
}

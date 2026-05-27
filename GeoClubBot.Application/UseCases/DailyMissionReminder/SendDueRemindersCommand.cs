using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.GeoGuessrAccountLinking;

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

            var sent = await directMessageAccess
                .SendDirectMessageAsync(reminder.DiscordUserId, message, cancellationToken)
                .ConfigureAwait(false);

            if (sent)
            {
                reminder.MarkSent(today);
                LogReminderSent(reminder.DiscordUserId);
            }
            else
            {
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

        if (linkedUser is null)
        {
            return false;
        }

        var clubMember = await members.ReadClubMemberByUserIdAsync(linkedUser.UserId, cancellationToken).ConfigureAwait(false);

        if (clubMember?.ClubId is null)
        {
            return false;
        }

        var todaysActivities = await activityReader
            .ReadTodaysActivitiesAsync(clubMember.ClubId.Value, cancellationToken)
            .ConfigureAwait(false);

        return todaysActivities.Any(a => a.UserId == linkedUser.UserId && a.XpReward == dailyMissionXpReward);
    }

    [LoggerMessage(LogLevel.Information, "Sending {Count} daily mission reminders.")]
    partial void LogSendingReminders(int count);

    [LoggerMessage(LogLevel.Debug, "Daily mission reminder sent to user {DiscordUserId}.")]
    partial void LogReminderSent(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Failed to send daily mission reminder to user {DiscordUserId}.")]
    partial void LogReminderFailed(ulong discordUserId);

    [LoggerMessage(LogLevel.Debug, "Daily mission reminder skipped for user {DiscordUserId} - already completed today.")]
    partial void LogReminderSkippedAlreadyDone(ulong discordUserId);
}

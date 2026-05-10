using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.DailyMissionReminder;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.DailyMissionReminder;

public partial class SendDueRemindersUseCase(
    IUnitOfWork unitOfWork,
    IDiscordDirectMessageAccess directMessageAccess,
    IGetLinkedGeoGuessrUserUseCase getLinkedGeoGuessrUserUseCase,
    IGeoGuessrActivityReader activityReader,
    IOptions<DailyMissionReminderConfiguration> config,
    ILogger<SendDueRemindersUseCase> logger) : ISendDueRemindersUseCase
{
    public async Task SendDueRemindersAsync()
    {
        var now = DateTime.UtcNow;
        var currentTime = new TimeOnly(now.Hour, now.Minute);
        var today = DateOnly.FromDateTime(now);

        var dueReminders = await unitOfWork.DailyMissionReminders
            .ReadDueRemindersAsync(currentTime, today)
            .ConfigureAwait(false);

        if (dueReminders.Count == 0)
        {
            return;
        }

        LogSendingReminders(dueReminders.Count);

        var defaultMessage = config.Value.DefaultMessage;
        var dailyMissionXpReward = config.Value.DailyMissionXpReward;

        foreach (var reminder in dueReminders)
        {
            var alreadyDone = await _hasUserCompletedDailyMissionTodayAsync(
                    reminder.DiscordUserId, dailyMissionXpReward)
                .ConfigureAwait(false);

            if (alreadyDone)
            {
                reminder.LastSentDateUtc = today;
                LogReminderSkippedAlreadyDone(reminder.DiscordUserId);
                continue;
            }

            var message = string.IsNullOrWhiteSpace(reminder.CustomMessage)
                ? defaultMessage
                : reminder.CustomMessage;

            var sent = await directMessageAccess
                .SendDirectMessageAsync(reminder.DiscordUserId, message)
                .ConfigureAwait(false);

            if (sent)
            {
                reminder.LastSentDateUtc = today;
                LogReminderSent(reminder.DiscordUserId);
            }
            else
            {
                LogReminderFailed(reminder.DiscordUserId);
            }
        }

        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task<bool> _hasUserCompletedDailyMissionTodayAsync(ulong discordUserId, int dailyMissionXpReward)
    {
        var linkedUser = await getLinkedGeoGuessrUserUseCase
            .GetLinkedGeoGuessrUserAsync(discordUserId)
            .ConfigureAwait(false);

        if (linkedUser is null)
        {
            return false;
        }

        var clubMember = await unitOfWork.ClubMembers
            .ReadClubMemberByUserIdAsync(linkedUser.UserId)
            .ConfigureAwait(false);

        if (clubMember?.ClubId is null)
        {
            return false;
        }

        var todaysActivities = await activityReader
            .ReadTodaysActivitiesAsync(clubMember.ClubId.Value)
            .ConfigureAwait(false);

        return todaysActivities.Any(a =>
            a.UserId == linkedUser.UserId && a.XpReward == dailyMissionXpReward);
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

using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Rendering;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;
using DomainDailyMissionReminder = Entities.DailyMissionReminder;

namespace UseCases.UseCases.DailyMissionReminder;

public sealed record SendDueRemindersCommand : ICommand;

/// <summary>
/// Sends reminders that were missed while the bot was down: reminders whose time already passed
/// today but that were not sent today. Sent once at startup. Reminders still ahead of "now" fire
/// via the regular schedule, and misses from previous days are moot (those missions expired), so
/// each user gets at most one catch-up DM no matter how long the bot was offline.
/// </summary>
public sealed record CatchUpMissedRemindersCommand : ICommand;

public sealed partial class SendDueRemindersHandler(
    IDailyMissionReminderRepository reminders,
    IClubMemberRepository members,
    IDiscordDirectMessageAccess directMessageAccess,
    ISender mediator,
    IGeoGuessrActivityReader activityReader,
    IDailyMissionRepository dailyMissions,
    IDailyMissionRenderer renderer,
    IOptions<DailyMissionReminderConfiguration> config,
    ILogger<SendDueRemindersHandler> logger)
    : IRequestHandler<SendDueRemindersCommand, Unit>,
      IRequestHandler<CatchUpMissedRemindersCommand, Unit>
{
    public async Task<Unit> Handle(SendDueRemindersCommand request, CancellationToken cancellationToken)
    {
        var (currentTime, today) = GetCurrentUtcMinuteAndDate();

        var dueReminders = await reminders
            .ReadDueRemindersForUpdateAsync(currentTime, today, cancellationToken)
            .ConfigureAwait(false);

        await SendRemindersAsync(dueReminders, today, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }

    public async Task<Unit> Handle(CatchUpMissedRemindersCommand request, CancellationToken cancellationToken)
    {
        var (currentTime, today) = GetCurrentUtcMinuteAndDate();

        var missedReminders = await reminders
            .ReadMissedRemindersForUpdateAsync(currentTime, today, cancellationToken)
            .ConfigureAwait(false);

        if (missedReminders.Count > 0)
        {
            LogCatchingUpMissedReminders(missedReminders.Count);
        }

        await SendRemindersAsync(missedReminders, today, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }

    private static (TimeOnly CurrentTime, DateOnly Today) GetCurrentUtcMinuteAndDate()
    {
        var now = DateTime.UtcNow;
        return (new TimeOnly(now.Hour, now.Minute), DateOnly.FromDateTime(now));
    }

    // Sends the given reminders, one DM per user. In the regular per-minute run each user has at
    // most one due reminder (adding at an existing time updates it instead of duplicating), so the
    // grouping is a no-op there; after a catch-up sweep a user may have missed several reminders
    // and only the latest one is sent, so a restart never bursts multiple DMs at the same person.
    private async Task SendRemindersAsync(List<DomainDailyMissionReminder> dueReminders, DateOnly today, CancellationToken cancellationToken)
    {
        if (dueReminders.Count == 0)
        {
            return;
        }

        LogSendingReminders(dueReminders.Count);

        var defaultMessage = config.Value.DefaultMessage;
        var dailyMissionXpReward = config.Value.DailyMissionXpReward;

        // Today's mission text is the same for everyone, so render it once on demand and reuse it.
        // Left null until the first reminder that will actually be sent, so an all-skipped run does no DB query.
        string? missionText = null;

        foreach (var userReminders in dueReminders.GroupBy(r => r.DiscordUserId))
        {
            var discordUserId = userReminders.Key;
            var reminder = userReminders.MaxBy(r => r.ReminderTimeUtc)!;

            var alreadyDone = await HasUserCompletedDailyMissionTodayAsync(discordUserId, dailyMissionXpReward, cancellationToken)
                .ConfigureAwait(false);

            if (alreadyDone)
            {
                MarkAllSent(userReminders, today);
                LogReminderSkippedAlreadyDone(discordUserId);
                continue;
            }

            missionText ??= await BuildMissionTextAsync(cancellationToken).ConfigureAwait(false);

            var template = string.IsNullOrWhiteSpace(reminder.CustomMessage)
                ? defaultMessage
                : reminder.CustomMessage;

            var message = template.Replace("{{mission_text}}", missionText).Trim();

            // A custom message that is only the placeholder collapses to empty when no missions are
            // stored; Discord rejects empty messages, so fall back to the default in that edge case.
            if (string.IsNullOrWhiteSpace(message))
            {
                message = defaultMessage.Replace("{{mission_text}}", missionText).Trim();
            }

            var dmResult = await directMessageAccess
                .SendDirectMessageAsync(discordUserId, message, cancellationToken)
                .ConfigureAwait(false);

            if (dmResult.IsSuccess)
            {
                MarkAllSent(userReminders, today);
                LogReminderSent(discordUserId);
            }
            else if (dmResult.Error.Code == DiscordDmErrorCodes.NoMutualGuild)
            {
                // The user has left the server but the reminder is still active. Normally it is
                // deactivated by the UserLeft event the moment they leave, so reaching here means
                // that event was missed (e.g. the bot was down when they left) — clean up and warn.
                foreach (var stale in userReminders)
                {
                    reminders.DeleteReminder(stale);
                }

                LogReminderUserLeftWhileActive(discordUserId);
            }
            else if (dmResult.Error.Type == ErrorType.Forbidden)
            {
                // The user has DMs from the bot disabled/blocked — retrying won't help until they fix it,
                // so mark it sent to stop this run (and the rest of the due window) from hammering Discord.
                MarkAllSent(userReminders, today);
                LogReminderDmsDisabled(discordUserId);
            }
            else
            {
                // Transient failure — left unmarked so a later run (or the next startup catch-up) can retry.
                LogReminderFailed(discordUserId);
            }
        }
    }

    private static void MarkAllSent(IEnumerable<DomainDailyMissionReminder> userReminders, DateOnly today)
    {
        foreach (var reminder in userReminders)
        {
            reminder.MarkSent(today);
        }
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

    private async Task<string> BuildMissionTextAsync(CancellationToken cancellationToken)
    {
        var missions = await dailyMissions.ReadLatestFetchedMissionsAsync(cancellationToken).ConfigureAwait(false);
        return string.Join("\n", missions.Select(m => renderer.RenderMission(ToDto(m))));
    }

    private static DailyMissionDto ToDto(DailyMission mission) => new()
    {
        Id = mission.MissionId,
        Type = mission.Type,
        GameMode = mission.GameMode,
        CurrentProgress = mission.CurrentProgress,
        TargetProgress = mission.TargetProgress,
        Completed = mission.Completed,
        EndDate = mission.EndDate,
        RewardAmount = mission.RewardAmount,
        RewardType = mission.RewardType,
        MapSlug = mission.MapSlug,
        MapName = mission.MapName
    };

    [LoggerMessage(LogLevel.Information, "Sending {Count} daily mission reminders.")]
    partial void LogSendingReminders(int count);

    [LoggerMessage(LogLevel.Information, "Found {Count} daily mission reminders that were missed while the bot was down.")]
    partial void LogCatchingUpMissedReminders(int count);

    [LoggerMessage(LogLevel.Debug, "Daily mission reminder sent to user {DiscordUserId}.")]
    partial void LogReminderSent(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Failed to send daily mission reminder to user {DiscordUserId}.")]
    partial void LogReminderFailed(ulong discordUserId);

    [LoggerMessage(LogLevel.Error, "Could not deliver daily mission reminder to user {DiscordUserId} - they have DMs from the bot disabled or blocked the bot; not retrying today.")]
    partial void LogReminderDmsDisabled(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Daily mission reminder for user {DiscordUserId} was still active after they left the server (UserLeft event missed); deactivating it now.")]
    partial void LogReminderUserLeftWhileActive(ulong discordUserId);

    [LoggerMessage(LogLevel.Debug, "Daily mission reminder skipped for user {DiscordUserId} - already completed today.")]
    partial void LogReminderSkippedAlreadyDone(ulong discordUserId);
}

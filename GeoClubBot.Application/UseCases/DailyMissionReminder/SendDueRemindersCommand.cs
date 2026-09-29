using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;
using DomainDailyMissionReminder = Entities.DailyMissionReminder;

namespace UseCases.UseCases.DailyMissionReminder;

public sealed record SendDueRemindersCommand : ICommand;

/// <summary>
/// Sends reminders that were missed while the bot was down: reminders whose time already passed
/// today but that were not sent today. Sent once at startup. Reminders still ahead of "now" fire
/// via the regular schedule, and misses from previous days are moot (that day is over), so
/// each user gets at most one catch-up DM no matter how long the bot was offline.
/// </summary>
public sealed record CatchUpMissedRemindersCommand : ICommand;

public sealed partial class SendDueRemindersHandler(
    IDailyMissionReminderRepository reminders,
    IClubMemberRepository members,
    IDiscordDirectMessageAccess directMessageAccess,
    ISender mediator,
    IGeoGuessrActivityReader activityReader,
    ClubActivityKindClassifier activityKinds,
    IClubMissionBoardReader boardReader,
    IOptions<DailyMissionReminderConfiguration> config,
    IOptions<MissionBoardConfiguration> missionBoardConfig,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
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

        foreach (var userReminders in dueReminders.GroupBy(r => r.DiscordUserId))
        {
            var discordUserId = userReminders.Key;
            var reminder = userReminders.MaxBy(r => r.ReminderTimeUtc)!;

            var progress = await GetProgressAsync(discordUserId, cancellationToken).ConfigureAwait(false);

            // The streak and the board are independent, so the reminder is only pointless once
            // neither needs anything. Someone who played a duel but hasn't claimed still gets nagged.
            var outstandingText = ReminderOutstandingText.Build(progress, config.Value);
            if (outstandingText is null)
            {
                MarkAllSent(userReminders, today);
                LogReminderSkippedAlreadyDone(discordUserId);
                continue;
            }

            var template = string.IsNullOrWhiteSpace(reminder.CustomMessage)
                ? defaultMessage
                : reminder.CustomMessage;

            var message = RenderMessage(template, outstandingText);

            // A custom message that collapses to empty - it was only placeholders, and nothing was
            // substituted - would be rejected by Discord, so fall back to the default in that case.
            if (string.IsNullOrWhiteSpace(message))
            {
                message = RenderMessage(defaultMessage, outstandingText);
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

    /// <summary>
    /// What the user has done today and where they stand on their club's mission board. An
    /// unlinked user, or one not on a tracked club roster, counts as not having played - we can't
    /// see their activity, so we still remind.
    /// </summary>
    private async Task<ReminderProgress> GetProgressAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        var linkedUser = await mediator
            .Send(new GetLinkedGeoGuessrUserQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);

        if (linkedUser.IsFailure)
        {
            return ReminderProgress.Unlinked;
        }

        var userId = linkedUser.Value.UserId;
        var clubMember = await members.ReadClubMemberByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        if (clubMember?.ClubId is not { } clubId)
        {
            return new ReminderProgress(ChallengeDone: false, ReminderMissionState.Unknown);
        }

        var todaysActivities = await activityReader
            .ReadTodaysActivitiesAsync(clubId, cancellationToken)
            .ConfigureAwait(false);

        var challengeDone = todaysActivities.Any(a => a.UserId == userId && activityKinds.IsDailyChallenge(a));

        var clubEntry = geoGuessrConfig.Value.Clubs.FirstOrDefault(c => c.ClubId == clubId);
        if (clubEntry is null || clubEntry.RemindMissions == false)
        {
            return new ReminderProgress(challengeDone, ReminderMissionState.Unknown);
        }

        var board = await boardReader.ReadCurrentAsync(clubId, cancellationToken).ConfigureAwait(false);
        if (board is null)
        {
            return new ReminderProgress(challengeDone, ReminderMissionState.Unknown);
        }

        var now = DateTimeOffset.UtcNow;
        var cycleStart = board.ClaimCycleStart(now, missionBoardConfig.Value.FallbackClaimCycleStart);

        return new ReminderProgress(
            challengeDone,
            ReminderProgress.From(board.ClaimStateOf(userId, cycleStart)),
            FreeCount: board.FreeTiles.Count,
            BoardNumber: board.CurrentBoardNumber,
            NextClaimReset: cycleStart.AddDays(1),
            OpenMission: board.OpenClaimOf(userId));
    }

    private static string RenderMessage(string template, string outstandingText) =>
        template
            .Replace("{{outstanding_text}}", outstandingText)
            // The reminder used to carry a second placeholder for the mission list, which is now
            // part of the outstanding text. Messages stored back then still contain it, so it maps
            // onto the same text instead of being DMed to the user verbatim.
            .Replace("{{mission_text}}", outstandingText)
            .Trim();

    [LoggerMessage(LogLevel.Information, "Sending {Count} daily reminders.")]
    partial void LogSendingReminders(int count);

    [LoggerMessage(LogLevel.Information, "Found {Count} daily reminders that were missed while the bot was down.")]
    partial void LogCatchingUpMissedReminders(int count);

    [LoggerMessage(LogLevel.Debug, "Daily reminder sent to user {DiscordUserId}.")]
    partial void LogReminderSent(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Failed to send daily reminder to user {DiscordUserId}.")]
    partial void LogReminderFailed(ulong discordUserId);

    [LoggerMessage(LogLevel.Error, "Could not deliver daily reminder to user {DiscordUserId} - they have DMs from the bot disabled or blocked the bot; not retrying today.")]
    partial void LogReminderDmsDisabled(ulong discordUserId);

    [LoggerMessage(LogLevel.Warning, "Daily reminder for user {DiscordUserId} was still active after they left the server (UserLeft event missed); deactivating it now.")]
    partial void LogReminderUserLeftWhileActive(ulong discordUserId);

    [LoggerMessage(LogLevel.Debug, "Daily reminder skipped for user {DiscordUserId} - nothing outstanding.")]
    partial void LogReminderSkippedAlreadyDone(ulong discordUserId);
}

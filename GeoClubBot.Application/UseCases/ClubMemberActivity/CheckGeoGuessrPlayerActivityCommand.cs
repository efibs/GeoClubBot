using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Notifications;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record CheckGeoGuessrPlayerActivityCommand(Guid ClubId)
    : ICommand<List<ClubMemberActivityStatus>>;

public sealed partial class CheckGeoGuessrPlayerActivityHandler(
    ActivityCheckSyncStep syncStep,
    ActivityStatusCalculator statusCalculator,
    ActivityAverageXpRollupStep averageXpStep,
    IExcusesRepository excuses,
    IHistoryRepository history,
    IClubRepository clubs,
    IUnitOfWork unitOfWork,
    IActivityStatusMessageSender activityStatusMessageSender,
    IActivityReportPublishGate publishGate,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig,
    ILogger<CheckGeoGuessrPlayerActivityHandler> logger)
    : IRequestHandler<CheckGeoGuessrPlayerActivityCommand, List<ClubMemberActivityStatus>>
{
    public async Task<List<ClubMemberActivityStatus>> Handle(CheckGeoGuessrPlayerActivityCommand request, CancellationToken cancellationToken)
    {
        var clubId = request.ClubId;
        var clubEntry = geoGuessrConfig.Value.GetClub(clubId);
        var defaults = activityCheckerConfig.Value;
        var xpRequirement = clubEntry.GetMinXP(defaults);
        var gracePeriod = TimeSpan.FromDays(clubEntry.GetGracePeriodDays(defaults));
        var maxNumStrikes = clubEntry.GetMaxNumStrikes(defaults);

        LogCheckingPlayerActivity(logger, clubId);

        // Ensure the club entity exists BEFORE syncing members. Both ClubMembers and
        // ClubMemberHistoryEntries have a required foreign key to Clubs, so syncing/recording a
        // not-yet-persisted club would fail. The club is configured (GetClub above would have thrown
        // otherwise) but its entity may not have been persisted yet — the initial club sync can fail or
        // not have run before this check fires. Creating it here keeps the checker self-sufficient
        // instead of throwing until the next club sync. The regular club sync later refreshes the
        // name/level and preserves the check time recorded below.
        var club = await clubs.ReadForUpdateByIdAsync(clubId, cancellationToken).ConfigureAwait(false);
        if (club is null)
        {
            club = clubs.CreateClub(Entities.Club.Create(clubId, clubId.ToString(), level: 0));
        }

        var members = await syncStep.ExecuteAsync(clubId, cancellationToken).ConfigureAwait(false);

        var latestHistoryEntries = await history
            .ReadLatestHistoryEntryProjectionsByClubIdAsync(clubId, cancellationToken)
            .ConfigureAwait(false);
        var allExcuses = await excuses.ReadExcuseProjectionsAsync(cancellationToken).ConfigureAwait(false);

        // The last check time is tracked on the club itself and is the single source of truth for the
        // activity-check interval. We deliberately do NOT derive it from the newest history entry: a
        // club with no members writes no history, yet a check still ran and must move the interval on.
        var lastActivityCheckTime = club.LatestActivityCheckTime ?? DateTimeOffset.MinValue;

        LogLastActivityCheckTime(logger, lastActivityCheckTime);

        var now = DateTimeOffset.UtcNow;

        var newLatestHistoryEntries = members.ToDictionary(
            m => m.User.UserId,
            m => ClubMemberHistoryEntry.Create(m.User.UserId, clubId, m.Xp, now));
        history.CreateHistoryEntries(newLatestHistoryEntries.Values);

        // Record the check on the club unconditionally: a check ran regardless of the member count.
        club.RecordActivityCheck(now);

        var newStatuses = await statusCalculator.ExecuteAsync(
                members, latestHistoryEntries, allExcuses, lastActivityCheckTime, now,
                xpRequirement, gracePeriod, maxNumStrikes, cancellationToken)
            .ConfigureAwait(false);

        var clubName = club.Name;

        // Flush before reporting. The average-XP rollup re-reads the history table through the
        // repositories (AsNoTracking → straight to the database), so the snapshots recorded above
        // must already be committed. Left in the change tracker until the UnitOfWorkBehavior commits
        // after this handler, they are invisible to that query: the newest interval — the one this
        // check just closed — would be missing and the average would silently cover the N intervals
        // BEFORE it. Committing here also means the recorded check time and strikes survive a
        // failure while messaging Discord, instead of being replayed on the next check.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var averageXpTopN = clubEntry.GetAverageXpTopN(defaults);
        var averageXpBottomN = clubEntry.GetAverageXpBottomN(defaults);

        // Clubs are checked in parallel but share one report channel. Hold the publish gate across
        // the whole send region so all of this club's messages land contiguously, before any other
        // club's messages start.
        await using (await publishGate.AcquireAsync(cancellationToken).ConfigureAwait(false))
        {
            await activityStatusMessageSender
                .SendActivityStatusUpdateMessageAsync(newStatuses, clubName, xpRequirement, cancellationToken)
                .ConfigureAwait(false);

            if (averageXpTopN.HasValue || averageXpBottomN.HasValue)
            {
                await averageXpStep.ExecuteAsync(
                        clubId, clubName, averageXpTopN, averageXpBottomN,
                        clubEntry.GetAverageXpHistoryDepth(defaults), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        LogPlayerActivityCheckDone(logger, clubId);

        return newStatuses;
    }

    [LoggerMessage(LogLevel.Debug, "Checking player activity for club {ClubId}...")]
    static partial void LogCheckingPlayerActivity(ILogger<CheckGeoGuessrPlayerActivityHandler> logger, Guid clubId);

    [LoggerMessage(LogLevel.Information, "Last activity check was on {LastActivityCheckTime:F}")]
    static partial void LogLastActivityCheckTime(ILogger<CheckGeoGuessrPlayerActivityHandler> logger, DateTimeOffset lastActivityCheckTime);

    [LoggerMessage(LogLevel.Debug, "Checking player activity for club {ClubId} done.")]
    static partial void LogPlayerActivityCheckDone(ILogger<CheckGeoGuessrPlayerActivityHandler> logger, Guid clubId);
}

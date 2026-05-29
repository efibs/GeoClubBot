using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record CheckGeoGuessrPlayerActivityCommand(Guid ClubId)
    : ICommand<List<ClubMemberActivityStatus>>;

public sealed class CheckGeoGuessrPlayerActivityHandler(
    ActivityCheckSyncStep syncStep,
    ActivityStatusCalculator statusCalculator,
    ActivityAverageXpRollupStep averageXpStep,
    IExcusesRepository excuses,
    IHistoryRepository history,
    IClubRepository clubs,
    IActivityStatusMessageSender activityStatusMessageSender,
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

        logger.LogDebug("Checking player activity for club {ClubId}...", clubId);

        var members = await syncStep.ExecuteAsync(clubId, cancellationToken).ConfigureAwait(false);

        var latestHistoryEntries = await history
            .ReadLatestHistoryEntryProjectionsByClubIdAsync(clubId, cancellationToken)
            .ConfigureAwait(false);
        var allExcuses = await excuses.ReadExcuseProjectionsAsync(cancellationToken).ConfigureAwait(false);

        var lastActivityCheckTime = latestHistoryEntries.Any()
            ? latestHistoryEntries.Select(a => a.Timestamp).Max()
            : DateTimeOffset.MinValue;

        logger.LogInformation("Last activity check was on {LastActivityCheckTime:F}", lastActivityCheckTime);

        var now = DateTimeOffset.UtcNow;

        var newLatestHistoryEntries = members.ToDictionary(
            m => m.User.UserId,
            m => ClubMemberHistoryEntry.Create(m.User.UserId, clubId, m.Xp, now));
        history.CreateHistoryEntries(newLatestHistoryEntries.Values);

        var newStatuses = await statusCalculator.ExecuteAsync(
                members, latestHistoryEntries, allExcuses, lastActivityCheckTime, now,
                xpRequirement, gracePeriod, maxNumStrikes, cancellationToken)
            .ConfigureAwait(false);

        var club = await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false);
        var clubName = club?.Name ?? clubId.ToString();

        await activityStatusMessageSender
            .SendActivityStatusUpdateMessageAsync(newStatuses, clubName, xpRequirement, cancellationToken)
            .ConfigureAwait(false);

        var averageXpTopN = clubEntry.GetAverageXpTopN(defaults);
        var averageXpBottomN = clubEntry.GetAverageXpBottomN(defaults);

        if (averageXpTopN.HasValue || averageXpBottomN.HasValue)
        {
            await averageXpStep.ExecuteAsync(
                    clubId, clubName, averageXpTopN, averageXpBottomN,
                    clubEntry.GetAverageXpHistoryDepth(defaults), cancellationToken)
                .ConfigureAwait(false);
        }

        logger.LogDebug("Checking player activity for club {ClubId} done.", clubId);

        return newStatuses;
    }
}

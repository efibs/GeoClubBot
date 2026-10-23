using Configuration;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Notifications;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;

/// <summary>
/// Optional last phase of <see cref="CheckGeoGuessrPlayerActivityHandler"/>: report the members
/// slowest to finish the board missions they claimed within <c>MissionClaimTracking:AverageWindow</c>,
/// and everyone who left a claim unfinished — the claims nobody else can take while they are held.
/// The configured window rather than the check window: about a week of claims is too few to tell
/// a habit from a bad week.
/// </summary>
public sealed class MissionCompletionReportStep(
    IMissionClaimRepository missionClaims,
    IActivityStatusMessageSender messageSender,
    IOptions<MissionClaimTrackingConfiguration> claimTrackingConfig)
{
    public async Task ExecuteAsync(
        Guid clubId,
        IReadOnlyCollection<ClubMember> members,
        DateTimeOffset now,
        int slowestN,
        CancellationToken cancellationToken)
    {
        var options = claimTrackingConfig.Value;
        if (!options.Enabled)
        {
            return;
        }

        var from = now - options.AverageWindow;
        var claims = await missionClaims
            .ReadClaimsOfClubAsync(clubId, MissionCompletionStats.ReadFrom(from), now, cancellationToken)
            .ConfigureAwait(false);
        var claimsByUser = claims.ToLookup(c => c.UserId);

        // Only current members: someone who left is no longer the club's concern.
        var completions = members
            .Where(m => claimsByUser.Contains(m.User.UserId))
            .Select(m => new ClubMemberMissionCompletion(
                m.User.Nickname,
                m.User.UserId,
                MissionCompletionStats.Compute(claimsByUser[m.User.UserId], from, now)))
            .Where(c => !c.Times.IsEmpty)
            .ToList();

        var slowest = completions
            .Where(c => c.Times.AverageCompletionTime is not null)
            .OrderByDescending(c => c.Times.AverageCompletionTime)
            .ThenBy(c => c.Nickname, StringComparer.OrdinalIgnoreCase)
            .Take(slowestN)
            .ToList();

        var unfinished = completions
            .Where(c => c.Times.AbandonedCount > 0 || c.Times.OpenClaimAge is not null)
            .OrderByDescending(c => c.Times.AbandonedCount)
            .ThenByDescending(c => c.Times.OpenClaimAge)
            .ThenBy(c => c.Nickname, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (slowest.Count > 0 || unfinished.Count > 0)
        {
            await messageSender
                .SendMissionCompletionMessageAsync(slowest, unfinished, options.AverageWindow, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

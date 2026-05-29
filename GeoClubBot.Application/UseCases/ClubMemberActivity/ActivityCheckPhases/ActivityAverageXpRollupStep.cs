using MediatR;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;

/// <summary>
/// Third phase of <see cref="CheckGeoGuessrPlayerActivityHandler"/>: dispatch the average-XP
/// calculation, slice into top/bottom cohorts, and message the result. Only invoked when
/// the club is configured to surface either ranking.
/// </summary>
public sealed class ActivityAverageXpRollupStep(
    ISender mediator,
    IActivityStatusMessageSender messageSender)
{
    public async Task ExecuteAsync(
        Guid clubId,
        string clubName,
        int? averageXpTopN,
        int? averageXpBottomN,
        int historyDepth,
        CancellationToken cancellationToken)
    {
        var averageXpResults = await mediator
            .Send(new CalculateAverageXpQuery(clubId, historyDepth), cancellationToken)
            .ConfigureAwait(false);

        var topMembers = averageXpTopN.HasValue
            ? averageXpResults
                .OrderByDescending(m => m.AverageXp)
                .ThenBy(m => m.JoinedAt)
                .Take(averageXpTopN.Value).ToList()
            : [];

        var topNicknames = topMembers.Select(m => m.Nickname).ToHashSet();
        var bottomMembers = averageXpBottomN.HasValue
            ? averageXpResults
                .Where(m => !topNicknames.Contains(m.Nickname))
                .OrderBy(m => m.AverageXp)
                .ThenByDescending(m => m.JoinedAt)
                .Take(averageXpBottomN.Value)
                .ToList()
            : [];

        if (topMembers.Count > 0 || bottomMembers.Count > 0)
        {
            await messageSender
                .SendAverageXpMessageAsync(topMembers, bottomMembers, clubName, historyDepth, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

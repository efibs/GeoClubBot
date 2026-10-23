namespace Entities;

/// <summary>
/// How long a member took to finish the board missions they claimed in a period.
/// </summary>
/// <param name="CompletedCount">Claimed missions that were completed.</param>
/// <param name="AverageCompletionTime">Average time from claim to completion; null when none were completed.</param>
/// <param name="AbandonedCount">Claimed missions whose board week ended before they were completed.</param>
/// <param name="OpenClaimAge">How long the member's still-open claim has been held; null when they hold none.</param>
public sealed record MissionCompletionTimes(
    int CompletedCount,
    TimeSpan? AverageCompletionTime,
    int AbandonedCount,
    TimeSpan? OpenClaimAge)
{
    public bool IsEmpty => CompletedCount == 0 && AbandonedCount == 0 && OpenClaimAge is null;
}

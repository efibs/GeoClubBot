using Entities;

namespace UseCases.UseCases.ClubMemberActivity;

/// <summary>
/// Sums up how long a member took to finish the board missions they claimed in a period. Views with
/// a period of their own (the last N days, the current check period) use it; the weekly report has
/// none worth judging habits on and uses <c>MissionClaimTracking:AverageWindow</c>. The average only
/// covers completed missions; missions left unfinished are counted apart, so a claim that was never
/// finished stays visible instead of disappearing from the average.
/// </summary>
public static class MissionCompletionStats
{
    /// <summary>
    /// Claims older than a period can still be open in it, so callers read this much further back
    /// than the period starts — one board week, the longest a claim can stay open.
    /// </summary>
    public static readonly TimeSpan OpenClaimLookback = TimeSpan.FromDays(7);

    /// <summary>The earliest claim time callers must read for <see cref="Compute"/> of a period starting at <paramref name="from"/>.</summary>
    public static DateTimeOffset ReadFrom(DateTimeOffset from) => from - OpenClaimLookback;

    /// <param name="claims">The member's claims since <see cref="ReadFrom"/>.</param>
    /// <param name="from">
    /// Start of the period. Completed and abandoned claims count when they were claimed within it;
    /// the open claim is reported whenever it was made, since it is still being held.
    /// </param>
    public static MissionCompletionTimes Compute(IEnumerable<MissionClaim> claims, DateTimeOffset from, DateTimeOffset now)
    {
        var list = claims.ToList();
        var inPeriod = list.Where(c => c.ClaimedAt >= from && c.ClaimedAt <= now).ToList();

        var completionTimes = inPeriod
            .Select(c => c.CompletionTime)
            .OfType<TimeSpan>()
            .ToList();

        TimeSpan? average = completionTimes.Count > 0
            ? TimeSpan.FromTicks((long)completionTimes.Average(t => (double)t.Ticks))
            : null;

        // A member can hold one open claim at a time; the oldest is the one to report should
        // GeoGuessr ever show more.
        var openClaimAge = list
            .Where(c => c.IsOpen(now))
            .Select(c => (TimeSpan?)(now - c.ClaimedAt))
            .Max();

        return new MissionCompletionTimes(
            completionTimes.Count,
            average,
            inPeriod.Count(c => c.IsAbandoned(now)),
            openClaimAge);
    }
}

using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.DailyMissionStatistics;

/// <summary>
/// Aggregated statistics over the logged daily missions of the last <paramref name="DaysBack"/>
/// UTC days, grouped per mission kind (Type + GameMode). A <c>null</c> <paramref name="ClubId"/>
/// computes completion rates across all clubs.
/// </summary>
public sealed record GetDailyMissionStatisticsQuery(Guid? ClubId, int DaysBack)
    : IQuery<Result<DailyMissionStatistics>>;

/// <summary>Distinct mission kinds ever logged; feeds the slash-command autocomplete.</summary>
public sealed record GetDailyMissionKindsQuery : IQuery<IReadOnlyList<DailyMissionKind>>;

public sealed record DailyMissionStatistics(
    string? ClubName,
    DateOnly FromDay,
    DateOnly ToDay,
    int DaysWithMissionData,
    int TotalMissionAppearances,
    double? AverageDayCompletionRate,
    IReadOnlyList<DailyMissionKindStatistics> Kinds);

public sealed record DailyMissionKindStatistics(
    string Type,
    string GameMode,
    int AppearanceCount,
    double AppearanceDayShare,
    double AverageTargetProgress,
    int MinTargetProgress,
    int MaxTargetProgress,
    DateOnly LastAppearance,
    double? AverageDayCompletionRateWhenPresent);

public sealed class DailyMissionStatisticsHandlers(
    IDailyMissionRepository dailyMissions,
    IDailyMissionCompletionRepository completions,
    IClubRepository clubs)
    : IRequestHandler<GetDailyMissionStatisticsQuery, Result<DailyMissionStatistics>>,
      IRequestHandler<GetDailyMissionKindsQuery, IReadOnlyList<DailyMissionKind>>
{
    public async Task<Result<DailyMissionStatistics>> Handle(
        GetDailyMissionStatisticsQuery request,
        CancellationToken cancellationToken)
    {
        var daysBack = Math.Clamp(request.DaysBack, 1, 365);
        var toDay = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDay = toDay.AddDays(-(daysBack - 1));

        string? clubName = null;
        if (request.ClubId is not null)
        {
            var club = await clubs.ReadClubByIdAsync(request.ClubId.Value, cancellationToken).ConfigureAwait(false);
            if (club is null)
            {
                return Error.NotFound("club.not_found", "The selected club is unknown.");
            }

            clubName = club.Name;
        }

        var fromUtc = new DateTimeOffset(fromDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(toDay.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var missions = await dailyMissions
            .ReadMissionsFetchedBetweenAsync(fromUtc, toUtc, cancellationToken)
            .ConfigureAwait(false);

        // The missions table is append-only and the same mission may be stored by several fetch
        // batches; keep only its first appearance and bucket it to that UTC day.
        var dedupedMissions = missions
            .GroupBy(m => m.MissionId)
            .Select(g => g.OrderBy(m => m.FetchedAtUtc).First())
            .Select(m => (Mission: m, Day: DateOnly.FromDateTime(m.FetchedAtUtc.UtcDateTime)))
            .ToList();

        var missionCountByDay = dedupedMissions
            .GroupBy(m => m.Day)
            .ToDictionary(g => g.Key, g => g.Count());

        var completionRateByDay = await ComputeCompletionRateByDayAsync(
                request.ClubId, fromDay, toDay, missionCountByDay, cancellationToken)
            .ConfigureAwait(false);

        var daysWithMissionData = missionCountByDay.Count;

        var kinds = dedupedMissions
            .GroupBy(m => (m.Mission.Type, m.Mission.GameMode))
            .Select(g =>
            {
                var presentDays = g.Select(m => m.Day).Distinct().ToList();
                var ratesWhenPresent = presentDays
                    .Where(completionRateByDay.ContainsKey)
                    .Select(d => completionRateByDay[d])
                    .ToList();

                return new DailyMissionKindStatistics(
                    Type: g.Key.Type,
                    GameMode: g.Key.GameMode,
                    AppearanceCount: g.Count(),
                    AppearanceDayShare: (double)presentDays.Count / daysWithMissionData,
                    AverageTargetProgress: g.Average(m => m.Mission.TargetProgress),
                    MinTargetProgress: g.Min(m => m.Mission.TargetProgress),
                    MaxTargetProgress: g.Max(m => m.Mission.TargetProgress),
                    LastAppearance: presentDays.Max(),
                    AverageDayCompletionRateWhenPresent: ratesWhenPresent.Count > 0 ? ratesWhenPresent.Average() : null);
            })
            .OrderByDescending(k => k.AppearanceCount)
            .ThenBy(k => k.Type)
            .ThenBy(k => k.GameMode)
            .ToList();

        return new DailyMissionStatistics(
            ClubName: clubName,
            FromDay: fromDay,
            ToDay: toDay,
            DaysWithMissionData: daysWithMissionData,
            TotalMissionAppearances: dedupedMissions.Count,
            AverageDayCompletionRate: completionRateByDay.Count > 0 ? completionRateByDay.Values.Average() : null,
            Kinds: kinds);
    }

    public Task<IReadOnlyList<DailyMissionKind>> Handle(
        GetDailyMissionKindsQuery request,
        CancellationToken cancellationToken) =>
        dailyMissions.ReadDistinctMissionKindsAsync(cancellationToken);

    /// <summary>
    /// Computes per day: completion events / (member rows × missions that day), capped at 100%.
    /// The snapshot's completion events carry no mission identity, so days with several missions
    /// get one blended rate. Only days with both a snapshot and logged missions are computable.
    /// </summary>
    private async Task<Dictionary<DateOnly, double>> ComputeCompletionRateByDayAsync(
        Guid? clubId,
        DateOnly fromDay,
        DateOnly toDay,
        Dictionary<DateOnly, int> missionCountByDay,
        CancellationToken cancellationToken)
    {
        var completionRows = await completions
            .ReadCompletionsAsync(clubId, fromDay, toDay, cancellationToken)
            .ConfigureAwait(false);

        return completionRows
            .GroupBy(c => c.Date)
            .Where(g => missionCountByDay.ContainsKey(g.Key))
            .ToDictionary(
                g => g.Key,
                g => Math.Min(1.0, (double)g.Sum(c => c.CompletedCount) / (g.Count() * missionCountByDay[g.Key])));
    }
}

using Entities;
using UseCases.Abstractions;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record GetLastCheckTimeQuery : IQuery<DateTimeOffset?>;

/// <summary>
/// The member's activity in the current check period — since their club's last weekly check,
/// which runs just after GeoGuessr's board reset — with progress towards the club's requirements.
/// </summary>
public sealed record GetActivityThisWeekQuery(string UserId) : IQuery<ClubMemberActivitySummary>;

public sealed record GetActivityLastDaysQuery(string UserId, int DaysBack) : IQuery<ClubMemberActivitySummary>;

public sealed record ClubStatisticsQuery : IQuery<ClubStatistics?>;

public sealed record PlayerStatisticsQuery(string Nickname) : IQuery<PlayerStatistics?>;

public sealed record GetActivityLeaderboardQuery(string? ClubName, int HistoryDepth)
    : IQuery<GetActivityLeaderboardResult>;

public sealed record GetActivityLeaderboardResult(List<ClubMemberAverageXp>? Leaderboard, string? ClubName);

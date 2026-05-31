using Entities;
using UseCases.Abstractions;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record GetLastCheckTimeQuery : IQuery<DateTimeOffset?>;

public sealed record GetActivityThisWeekQuery(string UserId) : IQuery<ClubMemberWeekActivity>;

public sealed record GetActivityLastDaysQuery(string UserId, int DaysBack) : IQuery<ClubMemberWeekActivity>;

public sealed record ClubStatisticsQuery : IQuery<ClubStatistics?>;

public sealed record PlayerStatisticsQuery(string Nickname) : IQuery<PlayerStatistics?>;

public sealed record GetActivityLeaderboardQuery(string? ClubName, int HistoryDepth)
    : IQuery<GetActivityLeaderboardResult>;

public sealed record GetActivityLeaderboardResult(List<ClubMemberAverageXp>? Leaderboard, string? ClubName);

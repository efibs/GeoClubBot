using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.Club;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record GetLastCheckTimeQuery : IQuery<DateTimeOffset?>;

public sealed record GetActivityThisWeekQuery(string UserId) : IQuery<ClubMemberWeekActivity>;

public sealed record ClubStatisticsQuery : IQuery<ClubStatistics?>;

public sealed record PlayerStatisticsQuery(string Nickname) : IQuery<PlayerStatistics?>;

public sealed record GetActivityLeaderboardQuery(string? ClubName, int HistoryDepth)
    : IQuery<GetActivityLeaderboardResult>;

public sealed record GetActivityLeaderboardResult(List<ClubMemberAverageXp>? Leaderboard, string? ClubName);

public sealed partial class ActivityQueriesHandler(
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IHistoryRepository history,
    IGeoGuessrActivityReader activityReader,
    ISender mediator,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<DailyMissionReminderConfiguration> missionConfig,
    ILogger<ActivityQueriesHandler> logger)
    : IRequestHandler<GetLastCheckTimeQuery, DateTimeOffset?>,
      IRequestHandler<GetActivityThisWeekQuery, ClubMemberWeekActivity>,
      IRequestHandler<ClubStatisticsQuery, ClubStatistics?>,
      IRequestHandler<PlayerStatisticsQuery, PlayerStatistics?>,
      IRequestHandler<GetActivityLeaderboardQuery, GetActivityLeaderboardResult>
{
    private readonly Guid _mainClubId = geoGuessrConfig.Value.MainClub.ClubId;

    public async Task<DateTimeOffset?> Handle(GetLastCheckTimeQuery request, CancellationToken cancellationToken)
    {
        var club = await clubs.ReadClubByIdAsync(_mainClubId).ConfigureAwait(false);
        if (club is null)
        {
            LogClubNotFound(logger, _mainClubId);
        }
        return club?.LatestActivityCheckTime;
    }

    public async Task<ClubMemberWeekActivity> Handle(GetActivityThisWeekQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startOfWeek = GetStartOfWeek(today);
        var startOfWeekUtc = new DateTimeOffset(startOfWeek.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var daySlots = Enumerable.Range(0, today.DayNumber - startOfWeek.DayNumber + 1)
            .Select(i => startOfWeek.AddDays(i))
            .ToList();

        var clubMember = await clubMembers
            .ReadClubMemberByUserIdAsync(request.UserId)
            .ConfigureAwait(false);

        if (clubMember?.ClubId is null)
        {
            return new ClubMemberWeekActivity(
                TotalXp: 0,
                DailyMissions: daySlots.Select(d => new DayMissionStatus(d, false)).ToList(),
                JoinedThisWeek: false,
                JoinedDateTime: DateTimeOffset.UtcNow);
        }

        var weekActivities = await activityReader
            .ReadActivitiesSinceAsync(clubMember.ClubId.Value, startOfWeekUtc)
            .ConfigureAwait(false);

        var memberActivities = weekActivities.Where(a => a.UserId == request.UserId).ToList();

        var dailyMissionXpReward = missionConfig.Value.DailyMissionXpReward;
        var completedDays = memberActivities
            .Where(a => a.XpReward == dailyMissionXpReward)
            .Select(a => DateOnly.FromDateTime(a.RecordedAt.UtcDateTime))
            .ToHashSet();

        var dailyMissions = daySlots
            .Select(d => new DayMissionStatus(d, completedDays.Contains(d)))
            .ToList();

        var totalXp = memberActivities.Sum(a => a.XpReward);
        var joinedThisWeek = clubMember.JoinedAt >= startOfWeekUtc;

        return new ClubMemberWeekActivity(
            TotalXp: totalXp,
            DailyMissions: dailyMissions,
            JoinedThisWeek: joinedThisWeek,
            JoinedDateTime: clubMember.JoinedAt);
    }

    public async Task<ClubStatistics?> Handle(ClubStatisticsQuery request, CancellationToken cancellationToken)
    {
        var club = await clubs.ReadClubByIdAsync(_mainClubId).ConfigureAwait(false);
        if (club is null)
        {
            return null;
        }

        var entries = await history.ReadHistoryEntriesAsync(club.ClubId).ConfigureAwait(false);

        var averagePointsEarned = entries
            .GroupBy(e => e.UserId)
            .Select(g => g.Select(e => e.Xp).ToList())
            .Select(g => g
                .Zip(g.Prepend(0), (a, b) => a - b)
                .Average())
            .Order()
            .ToList();

        if (averagePointsEarned.Count == 0)
        {
            return null;
        }

        var averagePoints = averagePointsEarned.Average();
        var minPoints = averagePointsEarned.Min();
        var firstQuartilePoints = averagePointsEarned.Skip(averagePointsEarned.Count / 4).First();
        var medianPoints = averagePointsEarned.Skip(averagePointsEarned.Count / 2).First();
        var thirdQuartilePoints = averagePointsEarned.Skip(averagePointsEarned.Count * 3 / 4).First();
        var maxPoints = averagePointsEarned.Max();

        return new ClubStatistics(club.Name, averagePoints, minPoints, firstQuartilePoints, medianPoints,
            thirdQuartilePoints, maxPoints);
    }

    public async Task<PlayerStatistics?> Handle(PlayerStatisticsQuery request, CancellationToken cancellationToken)
    {
        var clubMember = await clubMembers
            .ReadClubMemberByNicknameAsync(request.Nickname)
            .ConfigureAwait(false);

        if (clubMember?.ClubId is null)
        {
            return null;
        }

        var entries = await history
            .ReadHistoryEntriesByPlayerNicknameAsync(request.Nickname, clubMember.ClubId.Value)
            .ConfigureAwait(false);

        if (entries is null)
        {
            return null;
        }

        if (entries.Count == 0)
        {
            return new PlayerStatistics(request.Nickname, DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, 0);
        }

        var earliestTime = entries.Select(e => e.Timestamp).Min();

        var points = entries
            .OrderBy(e => e.Timestamp)
            .Select(e => e.Xp)
            .ToList();

        var pointDifferences = points
            .Skip(1)
            .Zip(points, (a, b) => a - b)
            .ToList();

        var numEntries = pointDifferences.Count;
        var averagePoints = pointDifferences.Average();
        var minPoints = pointDifferences.Min();
        var firstQuartilePoints = pointDifferences.Skip(pointDifferences.Count / 4).First();
        var medianPoints = pointDifferences.Skip(pointDifferences.Count / 2).First();
        var thirdQuartilePoints = pointDifferences.Skip(pointDifferences.Count * 3 / 4).First();
        var maxPoints = pointDifferences.Max();

        return new PlayerStatistics(request.Nickname, earliestTime, numEntries, averagePoints, minPoints,
            firstQuartilePoints, medianPoints, thirdQuartilePoints, maxPoints);
    }

    public async Task<GetActivityLeaderboardResult> Handle(GetActivityLeaderboardQuery request, CancellationToken cancellationToken)
    {
        var club = await mediator
            .Send(new GetClubByNameOrDefaultQuery(request.ClubName), cancellationToken)
            .ConfigureAwait(false);

        if (club is null)
        {
            return new GetActivityLeaderboardResult(null, null);
        }

        var leaderboard = await mediator
            .Send(new CalculateAverageXpQuery(club.ClubId, request.HistoryDepth), cancellationToken)
            .ConfigureAwait(false);

        var topMembers = leaderboard
            .OrderByDescending(m => m.AverageXp)
            .ThenBy(m => m.JoinedAt)
            .ToList();

        return new GetActivityLeaderboardResult(topMembers, club.Name);
    }

    private static DateOnly GetStartOfWeek(DateOnly date)
    {
        var daysFromMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-daysFromMonday);
    }

    [LoggerMessage(LogLevel.Error, "Club with id {clubId} not found.")]
    static partial void LogClubNotFound(ILogger<ActivityQueriesHandler> logger, Guid clubId);
}

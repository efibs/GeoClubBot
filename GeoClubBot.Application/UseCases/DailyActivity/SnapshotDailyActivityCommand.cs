using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.DailyActivity;

/// <summary>
/// Persists, for every configured club, what each member earned club XP for on the previous UTC
/// day: daily challenges / duels played, board missions and board-clear bonuses, and the day's XP.
/// Runs shortly after midnight so the whole day's activity feed is final. One row is written per
/// member — including zero counts, so the row count per (club, day) is the denominator for rates.
/// </summary>
public sealed record SnapshotDailyActivityCommand : ICommand;

public sealed partial class SnapshotDailyActivityHandler(
    IClubMemberDailyActivityRepository dailyActivities,
    IClubMemberRepository clubMembers,
    IGeoGuessrActivityReader activityReader,
    ClubActivityKindClassifier activityKinds,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<SnapshotDailyActivityHandler> logger) : IRequestHandler<SnapshotDailyActivityCommand, Unit>
{
    public async Task<Unit> Handle(SnapshotDailyActivityCommand request, CancellationToken cancellationToken)
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var dayStartUtc = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEndUtc = dayStartUtc.AddDays(1);

        foreach (var configClub in geoGuessrConfig.Value.Clubs)
        {
            var clubId = configClub.ClubId;

            try
            {
                var alreadySnapshotted = await dailyActivities
                    .HasSnapshotForDayAsync(clubId, day, cancellationToken)
                    .ConfigureAwait(false);

                if (alreadySnapshotted)
                {
                    LogSnapshotSkipped(clubId, day);
                    continue;
                }

                var activities = await activityReader
                    .ReadActivitiesSinceAsync(clubId, dayStartUtc, cancellationToken)
                    .ConfigureAwait(false);

                var activitiesByUser = activities
                    .Where(a => a.RecordedAt >= dayStartUtc && a.RecordedAt < dayEndUtc)
                    .GroupBy(a => a.UserId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var members = await clubMembers
                    .ReadClubMembersByClubIdAsync(clubId, cancellationToken)
                    .ConfigureAwait(false);

                dailyActivities.AddRange(members.Select(m =>
                {
                    var mine = activitiesByUser.GetValueOrDefault(m.UserId) ?? [];
                    return ClubMemberDailyActivity.Create(
                        clubId,
                        m.UserId,
                        day,
                        dailyChallengeCount: mine.Count(activityKinds.IsDailyChallenge),
                        boardMissionCount: mine.Count(activityKinds.IsBoardMission),
                        boardClearBonusCount: mine.Count(activityKinds.IsBoardClearBonus),
                        xp: mine.Sum(a => a.XpReward));
                }));

                LogSnapshotWritten(clubId, day, members.Count);
            }
            catch (Exception ex)
            {
                // One club's feed failing must not lose the other clubs' snapshots.
                LogSnapshotFailed(ex, clubId, day);
            }
        }

        return Unit.Value;
    }

    [LoggerMessage(LogLevel.Debug, "Daily activity snapshot for club {ClubId} on {Day} already exists; skipping.")]
    partial void LogSnapshotSkipped(Guid clubId, DateOnly day);

    [LoggerMessage(LogLevel.Information, "Daily activity snapshot for club {ClubId} on {Day} written for {MemberCount} members.")]
    partial void LogSnapshotWritten(Guid clubId, DateOnly day, int memberCount);

    [LoggerMessage(LogLevel.Error, "Failed to snapshot daily activity for club {ClubId} on {Day}.")]
    partial void LogSnapshotFailed(Exception ex, Guid clubId, DateOnly day);
}

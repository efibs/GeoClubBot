using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.DailyMissionStatistics;

/// <summary>
/// Persists, for every configured club, how many daily-mission completions each member had on
/// the previous UTC day. Runs shortly after midnight so the whole day's activity feed is final.
/// One row is written per member — including zero counts, so the row count per (club, day) is
/// the denominator for completion rates.
/// </summary>
public sealed record SnapshotDailyMissionCompletionsCommand : ICommand;

public sealed partial class SnapshotDailyMissionCompletionsHandler(
    IDailyMissionCompletionRepository completions,
    IClubMemberRepository clubMembers,
    IGeoGuessrActivityReader activityReader,
    IOptions<DailyMissionReminderConfiguration> reminderConfig,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<SnapshotDailyMissionCompletionsHandler> logger) : IRequestHandler<SnapshotDailyMissionCompletionsCommand, Unit>
{
    public async Task<Unit> Handle(SnapshotDailyMissionCompletionsCommand request, CancellationToken cancellationToken)
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var dayStartUtc = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEndUtc = dayStartUtc.AddDays(1);
        var dailyMissionXpReward = reminderConfig.Value.DailyMissionXpReward;

        foreach (var configClub in geoGuessrConfig.Value.Clubs)
        {
            var clubId = configClub.ClubId;

            try
            {
                var alreadySnapshotted = await completions
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

                var completionCountsByUserId = activities
                    .Where(a => a.XpReward == dailyMissionXpReward
                                && a.RecordedAt >= dayStartUtc
                                && a.RecordedAt < dayEndUtc)
                    .GroupBy(a => a.UserId)
                    .ToDictionary(g => g.Key, g => g.Count());

                var members = await clubMembers
                    .ReadClubMembersByClubIdAsync(clubId, cancellationToken)
                    .ConfigureAwait(false);

                completions.AddRange(members.Select(m => DailyMissionMemberCompletion.Create(
                    clubId,
                    m.UserId,
                    day,
                    completionCountsByUserId.GetValueOrDefault(m.UserId))));

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

    [LoggerMessage(LogLevel.Debug, "Daily mission completion snapshot for club {ClubId} on {Day} already exists; skipping.")]
    partial void LogSnapshotSkipped(Guid clubId, DateOnly day);

    [LoggerMessage(LogLevel.Information, "Daily mission completion snapshot for club {ClubId} on {Day} written for {MemberCount} members.")]
    partial void LogSnapshotWritten(Guid clubId, DateOnly day, int memberCount);

    [LoggerMessage(LogLevel.Error, "Failed to snapshot daily mission completions for club {ClubId} on {Day}.")]
    partial void LogSnapshotFailed(Exception ex, Guid clubId, DateOnly day);
}

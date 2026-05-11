using Configuration;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.ClubMemberActivity;

public class GetActivityThisWeekUseCase(
    IUnitOfWork unitOfWork,
    IGeoGuessrActivityReader activityReader,
    IOptions<DailyMissionReminderConfiguration> missionConfig) : IGetActivityThisWeekUseCase
{
    public async Task<ClubMemberWeekActivity> GetCurrentWeekActivityForMemberAsync(string userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startOfWeek = _getStartOfWeek(today);
        var startOfWeekUtc = new DateTimeOffset(startOfWeek.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var daySlots = Enumerable.Range(0, today.DayNumber - startOfWeek.DayNumber + 1)
            .Select(i => startOfWeek.AddDays(i))
            .ToList();

        var clubMember = await unitOfWork.ClubMembers
            .ReadClubMemberByUserIdAsync(userId)
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

        var memberActivities = weekActivities.Where(a => a.UserId == userId).ToList();

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

    private static DateOnly _getStartOfWeek(DateOnly date)
    {
        var daysFromMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-daysFromMonday);
    }
}
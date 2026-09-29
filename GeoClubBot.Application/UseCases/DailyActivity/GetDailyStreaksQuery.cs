using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.DailyActivity;

/// <summary>
/// Computes each club member's daily streak (daily challenge or a duel played) over the trailing
/// <paramref name="WindowDays"/> window, ranked for the Club Dashboard Activity's streaks panel.
/// </summary>
public sealed record GetDailyStreaksQuery(Guid ClubId, int WindowDays)
    : IQuery<List<MemberDailyStreak>>;

public sealed record MemberDailyStreak(string Nickname, int CurrentStreak, int LongestStreak);

public sealed class GetDailyStreaksHandler(
    IClubMemberDailyActivityRepository dailyActivities,
    IClubMemberRepository members)
    : IRequestHandler<GetDailyStreaksQuery, List<MemberDailyStreak>>
{
    public async Task<List<MemberDailyStreak>> Handle(GetDailyStreaksQuery request, CancellationToken cancellationToken)
    {
        var windowDays = Math.Clamp(request.WindowDays, 1, 365);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDay = today.AddDays(-(windowDays - 1));

        var rows = await dailyActivities
            .ReadDailyActivitiesAsync(request.ClubId, fromDay, today, cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return [];
        }

        var clubMembers = await members
            .ReadClubMembersByClubIdAsync(request.ClubId, cancellationToken)
            .ConfigureAwait(false);
        var nicknamesByUserId = clubMembers.ToDictionary(m => m.UserId, m => m.User.Nickname);

        // A day extends the streak when the member played the daily challenge or a duel. Rows
        // written before the bot tracked that are judged on the old daily mission instead, rather
        // than retroactively breaking streaks.
        var completedDaysByUser = rows
            .Where(r => r.ExtendsStreak)
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Date).ToHashSet());

        var results = new List<MemberDailyStreak>();
        foreach (var (userId, completedDays) in completedDaysByUser)
        {
            // Skip departed members we can no longer put a name to.
            if (!nicknamesByUserId.TryGetValue(userId, out var nickname))
            {
                continue;
            }

            results.Add(new MemberDailyStreak(
                nickname,
                CalculateCurrentStreak(completedDays, today),
                CalculateLongestStreak(completedDays)));
        }

        return results
            .OrderByDescending(s => s.CurrentStreak)
            .ThenByDescending(s => s.LongestStreak)
            .ThenBy(s => s.Nickname, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CalculateCurrentStreak(HashSet<DateOnly> completedDays, DateOnly today)
    {
        // Anchor at today, or yesterday if today's snapshot hasn't been taken yet (completion
        // snapshots run after midnight UTC for the previous day), so an active streak isn't
        // reported as broken just because the current day hasn't been recorded.
        DateOnly anchor;
        if (completedDays.Contains(today))
        {
            anchor = today;
        }
        else if (completedDays.Contains(today.AddDays(-1)))
        {
            anchor = today.AddDays(-1);
        }
        else
        {
            return 0;
        }

        var streak = 0;
        for (var day = anchor; completedDays.Contains(day); day = day.AddDays(-1))
        {
            streak++;
        }

        return streak;
    }

    private static int CalculateLongestStreak(HashSet<DateOnly> completedDays)
    {
        var longest = 0;
        foreach (var day in completedDays)
        {
            // Only start counting from the first day of each run (no completed predecessor).
            if (completedDays.Contains(day.AddDays(-1)))
            {
                continue;
            }

            var run = 0;
            for (var cursor = day; completedDays.Contains(cursor); cursor = cursor.AddDays(1))
            {
                run++;
            }

            longest = Math.Max(longest, run);
        }

        return longest;
    }
}

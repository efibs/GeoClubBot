namespace Entities;

/// <summary>
/// What a member earned club XP for on one UTC day, snapshotted from GeoGuessr's activity feed the
/// following night. The feed only reaches back a little over two weeks, so this is what keeps
/// longer history such as streaks.
/// </summary>
public class ClubMemberDailyActivity : BaseEntity
{
    public int Id { get; private set; }

    public Guid ClubId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    /// <summary>
    /// Completions of the old daily mission that day. GeoGuessr replaced the daily mission with the
    /// club mission board on 2026-09-23, so this is 0 from then on; it only matters for older rows
    /// without <see cref="DailyChallengeCount"/>.
    /// </summary>
    public int LegacyDailyMissionCount { get; private set; }

    /// <summary>
    /// Daily challenges or duels played that day — the daily streak.
    ///
    /// Nullable because rows written before the bot tracked it (before 2026-08-30) carry no
    /// information either way: <c>null</c> means "not tracked", not "did not happen". The streak
    /// query then falls back to <see cref="LegacyDailyMissionCount"/>.
    /// </summary>
    public int? DailyChallengeCount { get; private set; }

    /// <summary>Board missions credited to the member that day.</summary>
    public int BoardMissionCount { get; private set; }

    /// <summary>Board-clear bonuses credited to the member that day.</summary>
    public int BoardClearBonusCount { get; private set; }

    /// <summary>All club XP of the member that day; null on rows written before it was tracked.</summary>
    public int? Xp { get; private set; }

    public static ClubMemberDailyActivity Create(
        Guid clubId,
        string userId,
        DateOnly date,
        int? dailyChallengeCount,
        int boardMissionCount = 0,
        int boardClearBonusCount = 0,
        int? xp = null,
        int legacyDailyMissionCount = 0)
    {
        return new ClubMemberDailyActivity
        {
            ClubId = clubId,
            UserId = userId,
            Date = date,
            DailyChallengeCount = dailyChallengeCount,
            BoardMissionCount = boardMissionCount,
            BoardClearBonusCount = boardClearBonusCount,
            Xp = xp,
            LegacyDailyMissionCount = legacyDailyMissionCount
        };
    }

    /// <summary>Whether the day continues the member's daily streak.</summary>
    public bool ExtendsStreak => DailyChallengeCount is { } challenges
        ? challenges > 0
        : LegacyDailyMissionCount > 0;

    private ClubMemberDailyActivity()
    {
    }
}

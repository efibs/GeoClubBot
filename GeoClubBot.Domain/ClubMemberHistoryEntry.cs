namespace Entities;

public class ClubMemberHistoryEntry : BaseEntity
{
    public DateTimeOffset Timestamp { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public Guid ClubId { get; private set; }

    public int Xp { get; private set; }

    /// <summary>
    /// Rule XP (see <c>ActivityChecker:RuleXp</c>) of the interval that ends at this snapshot. Null
    /// for snapshots taken before rule XP existed; averages then fall back to the raw XP difference.
    /// </summary>
    public int? RuleXp { get; private set; }

    /// <summary>Daily challenge / duel entries in the interval ending here; null when not recorded.</summary>
    public int? StreakDays { get; private set; }

    /// <summary>Board missions credited in the interval ending here; null when not recorded.</summary>
    public int? BoardMissionCount { get; private set; }

    public ClubMember? ClubMember { get; private set; }

    public Club? Club { get; private set; }

    public static ClubMemberHistoryEntry Create(string userId, Guid clubId, int xp, DateTimeOffset timestamp)
    {
        return new ClubMemberHistoryEntry
        {
            UserId = userId,
            ClubId = clubId,
            Xp = xp,
            Timestamp = timestamp
        };
    }

    /// <summary>Records what the member did in the interval that ends at this snapshot.</summary>
    public void RecordInterval(int ruleXp, int streakDays, int boardMissionCount)
    {
        RuleXp = ruleXp;
        StreakDays = streakDays;
        BoardMissionCount = boardMissionCount;
    }

    private ClubMemberHistoryEntry()
    {
    }

    public override string ToString() => $"{Timestamp:d}: {Xp}XP";
}

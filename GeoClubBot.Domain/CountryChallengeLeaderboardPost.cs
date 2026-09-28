namespace Entities;

/// <summary>
/// Records that the country challenge leaderboard was posted on a day, so a manual run on the same day
/// does not post it a second time.
/// </summary>
public class CountryChallengeLeaderboardPost : BaseEntity
{
    public int Id { get; private set; }

    public DateOnly Date { get; private set; }

    public string Season { get; private set; } = string.Empty;

    public DateTimeOffset PostedAt { get; private set; }

    public static CountryChallengeLeaderboardPost Create(DateOnly date, string season, DateTimeOffset postedAt)
    {
        return new CountryChallengeLeaderboardPost
        {
            Date = date,
            Season = season,
            PostedAt = postedAt
        };
    }

    private CountryChallengeLeaderboardPost()
    {
    }
}

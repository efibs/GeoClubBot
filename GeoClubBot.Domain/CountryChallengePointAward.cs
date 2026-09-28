namespace Entities;

/// <summary>Where a point award came from.</summary>
public enum CountryChallengePointSource
{
    /// <summary>A place on the highscores of a country challenge the bot evaluated.</summary>
    Challenge,

    /// <summary>The standings kept by hand before the bot tracked them, imported by an admin.</summary>
    Import
}

/// <summary>
/// Points one player received towards the country challenge leaderboard of a season. The leaderboard
/// is the sum of these rows, so changing the season starts a fresh one without deleting anything.
/// </summary>
public class CountryChallengePointAward : BaseEntity
{
    public int Id { get; private set; }

    public string Season { get; private set; } = string.Empty;

    /// <summary>The GeoGuessr user id of the player.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>The player's nickname when the points were awarded.</summary>
    public string Nickname { get; private set; } = string.Empty;

    public int Points { get; private set; }

    /// <summary>The place on the challenge's highscores, 1-based. <c>null</c> for imported points.</summary>
    public int? Place { get; private set; }

    /// <summary>The evaluated challenge. <c>null</c> for imported points.</summary>
    public int? PostId { get; private set; }

    public CountryChallengePointSource Source { get; private set; }

    public DateTimeOffset AwardedAt { get; private set; }

    public static CountryChallengePointAward ForPlacement(
        string season,
        int postId,
        int place,
        string userId,
        string nickname,
        int points,
        DateTimeOffset awardedAt)
    {
        return new CountryChallengePointAward
        {
            Season = season,
            PostId = postId,
            Place = place,
            UserId = userId,
            Nickname = nickname,
            Points = points,
            Source = CountryChallengePointSource.Challenge,
            AwardedAt = awardedAt
        };
    }

    public static CountryChallengePointAward Imported(
        string season,
        string userId,
        string nickname,
        int points,
        DateTimeOffset awardedAt)
    {
        return new CountryChallengePointAward
        {
            Season = season,
            UserId = userId,
            Nickname = nickname,
            Points = points,
            Source = CountryChallengePointSource.Import,
            AwardedAt = awardedAt
        };
    }

    private CountryChallengePointAward()
    {
    }
}

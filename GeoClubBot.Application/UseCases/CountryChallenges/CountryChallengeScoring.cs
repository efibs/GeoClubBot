using Constants;
using Entities;

namespace UseCases.UseCases.CountryChallenges;

public static class CountryChallengeScoring
{
    /// <summary>The points each player earned, by position on the highscores: first gets <c>points[0]</c>.</summary>
    public static IReadOnlyList<int> PointsByPlace(int playerCount, IReadOnlyList<int> points) =>
        Enumerable.Range(0, playerCount).Select(i => i < points.Count ? points[i] : 0).ToList();

    /// <summary>An award for every player who earned points; places without points leave no row behind.</summary>
    public static List<CountryChallengePointAward> Awards(
        string season,
        CountryChallengePost post,
        IReadOnlyList<ClubChallengeResultPlayer> players,
        IReadOnlyList<int> pointsByPlace,
        DateTimeOffset awardedAt)
    {
        var awards = new List<CountryChallengePointAward>();

        for (var i = 0; i < players.Count && i < pointsByPlace.Count; i++)
        {
            if (pointsByPlace[i] <= 0)
            {
                continue;
            }

            awards.Add(CountryChallengePointAward.ForPlacement(
                season,
                post.Id,
                place: i + 1,
                players[i].UserId,
                Truncate(players[i].Nickname, StringLengthConstants.GeoGuessrPlayerNicknameMaxLength),
                pointsByPlace[i],
                awardedAt));
        }

        return awards;
    }

    public static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}

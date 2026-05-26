namespace Entities;

public class ClubChallengeLink : BaseEntity
{
    public int Id { get; private set; }

    public string Difficulty { get; private set; } = string.Empty;

    public int RolePriority { get; private set; }

    public string ChallengeId { get; private set; } = string.Empty;

    public static ClubChallengeLink Create(string difficulty, int rolePriority, string challengeId)
    {
        return new ClubChallengeLink
        {
            Difficulty = difficulty,
            RolePriority = rolePriority,
            ChallengeId = challengeId
        };
    }

    private ClubChallengeLink()
    {
    }
}

namespace Entities;

public class ClubChallengeLink
{
    public int Id { get; set; }

    public required string Difficulty { get; set; }

    public required int RolePriority { get; set; }
    
    public required string ChallengeId { get; set; }
}
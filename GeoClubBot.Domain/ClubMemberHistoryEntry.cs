namespace Entities;

public class ClubMemberHistoryEntry
{
    public required DateTimeOffset Timestamp { get; set; }
    
    public required string UserId { get; set; }

    public required int Xp { get; set; }
    
    public ClubMember? ClubMember { get; set; }

    public override string ToString()
    {
        return $"{Timestamp:d}: {Xp}XP";
    }
}
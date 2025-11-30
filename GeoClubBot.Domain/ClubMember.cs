namespace Entities;

public class ClubMember : BaseEntity
{
    public required string UserId { get; set; }
    
    public required Guid ClubId { get; set; }

    public required GeoGuessrUser User { get; set; }

    public required bool IsCurrentlyMember { get; set; }
    
    public required int Xp { get; set; }
    
    public required DateTimeOffset JoinedAt { get; set; }
    
    public required ulong? PrivateTextChannelId { get; set; }

    public List<ClubMemberHistoryEntry> History { get; set; } = [];

    public List<ClubMemberStrike> Strikes { get; set; } = [];

    public List<ClubMemberExcuse> Excuses { get; set; } = [];
    
    public override string ToString()
    {
        return User.ToString();
    }
}
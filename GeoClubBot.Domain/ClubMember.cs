namespace Entities;

public class ClubMember
{
    public required string UserId { get; set; }
    
    public required Guid ClubId { get; set; }
    
    public Club? Club { get; set; }

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

    public ClubMember DeepCopy()
    {
        return new ClubMember
        {
            UserId = UserId,
            ClubId = ClubId,
            Club = Club?.DeepCopy(),
            User = User.DeepCopy(),
            IsCurrentlyMember = IsCurrentlyMember,
            Xp = Xp,
            JoinedAt = JoinedAt,
            PrivateTextChannelId = PrivateTextChannelId,
            History = History.Select(e => e.DeepCopy()).ToList(),
            Strikes = Strikes.Select(s => s.DeepCopy()).ToList(),
            Excuses = Excuses.Select(e => e.DeepCopy()).ToList()
        };
    }

    public ClubMember ShallowCopy()
    {
        return new ClubMember
        {
            UserId = UserId,
            ClubId = ClubId,
            Club = Club,
            User = User,
            IsCurrentlyMember = IsCurrentlyMember,
            Xp = Xp,
            JoinedAt = JoinedAt,
            PrivateTextChannelId = PrivateTextChannelId,
            History = History,
            Strikes = Strikes,
            Excuses = Excuses
        };
    }
}
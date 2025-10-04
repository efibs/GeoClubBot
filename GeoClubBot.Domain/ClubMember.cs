using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Constants;

namespace Entities;

public class ClubMember
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(StringLengthConstants.GeoGuessrUserIdLength)]
    [ForeignKey(nameof(User))]
    public required string UserId { get; set; }
    
    [ForeignKey(nameof(Club))]
    public required Guid ClubId { get; set; }
    
    public Club? Club { get; set; }

    public required GeoGuessrUser User { get; set; }

    public required bool IsCurrentlyMember { get; set; }
    
    public required int Xp { get; set; }
    
    public required DateTimeOffset JoinedAt { get; set; }

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
            History = History.Select(e => e.DeepCopy()).ToList(),
            Strikes = Strikes.Select(s => s.DeepCopy()).ToList(),
            Excuses = Excuses.Select(e => e.DeepCopy()).ToList()
        };
    }
}
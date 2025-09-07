using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Constants;
using Microsoft.EntityFrameworkCore;

namespace Entities;

public class ClubMember
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(StringLengthConstants.GeoGuessrUserIdLength)]
    [ForeignKey(nameof(User))]
    public string UserId { get; set; } = string.Empty;
    
    [ForeignKey(nameof(Club))]
    public Guid ClubId { get; set; }
    
    public Club? Club { get; set; }

    public GeoGuessrUser? User { get; set; }
    
    public List<ClubMemberHistoryEntry>? History { get; set; }
    
    public List<ClubMemberStrike>? Strikes { get; set; }
    
    public List<ClubMemberExcuse>? Excuses { get; set; }

    public override string ToString()
    {
        return User?.ToString() ?? "N/A";
    }
}
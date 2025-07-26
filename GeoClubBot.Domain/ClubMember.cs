using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Constants;
using Microsoft.EntityFrameworkCore;

namespace Entities;

[Index(nameof(Nickname))]
public class ClubMember
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(StringLengthConstants.GeoGuessrUserIdLength)]
    public string UserId { get; set; } = string.Empty;
    
    [ForeignKey(nameof(Club))]
    public Guid ClubId { get; set; }

    [MaxLength(StringLengthConstants.GeoGuessrPlayerNicknameMaxLength)]
    public string Nickname { get; set; } = string.Empty;
    
    public Club? Club { get; set; }
    
    public List<ClubMemberHistoryEntry>? History { get; set; }
    
    public List<ClubMemberStrike>? Strikes { get; set; }
    
    public List<ClubMemberExcuse>? Excuses { get; set; }
}
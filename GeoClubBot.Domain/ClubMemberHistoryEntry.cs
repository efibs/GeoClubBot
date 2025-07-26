using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Constants;
using Microsoft.EntityFrameworkCore;

namespace Entities;

[PrimaryKey(nameof(Timestamp), nameof(UserId))]
public class ClubMemberHistoryEntry
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public DateTimeOffset Timestamp { get; set; }
    
    [ForeignKey(nameof(ClubMember))]
    [MaxLength(StringLengthConstants.GeoGuessrUserIdLength)]
    public string UserId { get; set; } = string.Empty;

    public int Xp { get; set; }
    
    public ClubMember? ClubMember { get; set; }

    public override string ToString()
    {
        return $"{Timestamp:d}: {Xp}XP";
    }
}
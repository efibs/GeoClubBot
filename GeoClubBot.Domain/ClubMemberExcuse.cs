using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Constants;
using Microsoft.EntityFrameworkCore;

namespace Entities;

[Index(nameof(To))]
public class ClubMemberExcuse
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid ExcuseId { get; set; }

    [ForeignKey(nameof(ClubMember))]
    [MaxLength(StringLengthConstants.GeoGuessrUserIdLength)]
    public string UserId { get; set; } = string.Empty;
    
    public DateTimeOffset From { get; set; }
    
    public DateTimeOffset To { get; set; }
    
    public ClubMember? ClubMember { get; set; }

    public override string ToString()
    {
        return $"{From:d} - {To:d} (Id: {ExcuseId})";
    }

    public string ToStringWithPlayerName()
    {
        return $"Player {ClubMember?.User?.Nickname ?? "N/A"}: {From:d} - {To:d} (Id: {ExcuseId})";
    }
}
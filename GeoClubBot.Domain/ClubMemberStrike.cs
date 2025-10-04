using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Constants;
using Microsoft.EntityFrameworkCore;

namespace Entities;

[Index(nameof(Timestamp))]
public class ClubMemberStrike
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid StrikeId { get; set; }

    [ForeignKey(nameof(ClubMember))]
    [MaxLength(StringLengthConstants.GeoGuessrUserIdLength)]
    public string UserId { get; set; } = string.Empty;
    
    public DateTimeOffset Timestamp { get; set; }
    
    public bool Revoked { get; set; }
    
    public ClubMember? ClubMember { get; set; }

    public override string ToString()
    {
        return $"{Timestamp:d} - Revoked: {Revoked} (Id: {StrikeId})";
    }
    
    public string ToStringDetailed(TimeSpan expirationTimeSpan)
    {
        // Get the expiration date
        var expiration = Timestamp + expirationTimeSpan;
        
        return $"Player {ClubMember?.User?.Nickname ?? "N/A"}: {Timestamp:d} - Revoked: {Revoked} (Id: {StrikeId}, expires: {expiration:d})";
    }

    public ClubMemberStrike DeepCopy()
    {
        return new ClubMemberStrike
        {
            StrikeId = StrikeId,
            UserId = UserId,
            Timestamp = Timestamp,
            Revoked = Revoked,
            ClubMember = ClubMember?.DeepCopy(),
        };
    }
}
namespace Entities;

public class ClubMemberStrike
{
    public required Guid StrikeId { get; set; }
    
    public required string UserId { get; set; }
    
    public required DateTimeOffset Timestamp { get; set; }
    
    public required bool Revoked { get; set; }
    
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
}
namespace Entities;

public class ClubMemberExcuse
{
    public Guid ExcuseId { get; set; }
    
    public required string UserId { get; set; }
    
    public required DateTimeOffset From { get; set; }
    
    public required DateTimeOffset To { get; set; }
    
    public ClubMember? ClubMember { get; set; }

    public override string ToString()
    {
        return $"{From:d} - {To:d} (Id: {ExcuseId})";
    }

    public string ToStringWithPlayerName()
    {
        return $"Player {ClubMember?.User?.Nickname ?? "N/A"}: {From:d} - {To:d} (Id: {ExcuseId})";
    }

    public ClubMemberExcuse DeepCopy()
    {
        return new ClubMemberExcuse
        {
            ExcuseId = ExcuseId,
            UserId = UserId,
            From = From,
            To = To,
            ClubMember = ClubMember?.DeepCopy()
        };
    }
}
namespace Entities;

public class Club
{
    public Guid ClubId { get; set; }
    
    public required string Name { get; set; }
    
    public int Level { get; set; }
    
    public DateTimeOffset? LatestActivityCheckTime { get; set; }

    public override string ToString()
    {
        return Name;
    }
}
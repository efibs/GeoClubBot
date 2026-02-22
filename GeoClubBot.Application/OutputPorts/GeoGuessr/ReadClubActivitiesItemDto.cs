namespace UseCases.OutputPorts.GeoGuessr;

public class ReadClubActivitiesItemDto
{
    public required string UserId { get; set; }
    
    public required int XpReward { get; set; }
    
    public required DateTimeOffset RecordedAt { get; set; }
}
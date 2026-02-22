namespace UseCases.OutputPorts.GeoGuessr;

public class ReadClubActivitiesResponseDto
{
    public required List<ReadClubActivitiesItemDto> Items { get; set; }
    
    public string? PaginationToken { get; set; }
}
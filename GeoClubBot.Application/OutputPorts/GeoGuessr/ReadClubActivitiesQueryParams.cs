namespace UseCases.OutputPorts.GeoGuessr;

public class ReadClubActivitiesQueryParams
{
    public int Limit { get; set; } = 25;

    public string? PaginationToken { get; set; } = null;
}
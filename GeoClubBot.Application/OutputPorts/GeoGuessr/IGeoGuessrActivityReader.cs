namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrActivityReader
{
    Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadTodaysActivitiesAsync(Guid clubId);
}

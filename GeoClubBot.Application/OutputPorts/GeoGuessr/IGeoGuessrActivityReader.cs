namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrActivityReader
{
    Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadTodaysActivitiesAsync(Guid clubId);
    Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadActivitiesSinceAsync(Guid clubId, DateTimeOffset since);
}

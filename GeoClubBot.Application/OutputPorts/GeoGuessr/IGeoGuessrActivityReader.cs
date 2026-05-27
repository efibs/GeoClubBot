namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrActivityReader
{
    Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadTodaysActivitiesAsync(Guid clubId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadActivitiesSinceAsync(Guid clubId, DateTimeOffset since, CancellationToken cancellationToken = default);
}

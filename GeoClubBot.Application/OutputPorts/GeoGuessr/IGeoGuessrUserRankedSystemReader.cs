namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrUserRankedSystemReader
{
    Task<RankedProgressResponseDto?> ReadRankedProgressOfUserAsync(string userId,
        CancellationToken cancellationToken = default);

    Task<RankedPeakRatingResponseDto?> ReadRankedPeakRatingOfUserAsync(string userId,
        CancellationToken cancellationToken = default);
}

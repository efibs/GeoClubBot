using Utilities;

namespace UseCases.OutputPorts.AI;

/// <summary>
/// Counts how often species are recorded in an area, from GBIF's occurrence records: the evidence of
/// what grows where a screenshot was taken, which the picture alone cannot give.
/// </summary>
public interface IPlantRecordCounter
{
    /// <summary>
    /// The records of each of <paramref name="taxonKeys"/> (GBIF species keys) within
    /// <paramref name="area"/>; a key with none is missing from the answer. Fails with
    /// <see cref="ErrorType.Unexpected"/> when GBIF cannot be read. Never throws for it.
    /// </summary>
    Task<Result<IReadOnlyDictionary<long, long>>> CountAsync(
        IReadOnlyCollection<long> taxonKeys,
        RecordArea area,
        CancellationToken cancellationToken = default);
}

/// <summary>Where records are counted.</summary>
public closed record RecordArea;

/// <param name="Code">ISO 3166 alpha-2.</param>
public sealed record CountryRecordArea(string Code) : RecordArea;

/// <summary>Within <paramref name="RadiusKm"/> of a point.</summary>
public sealed record CircleRecordArea(double Latitude, double Longitude, double RadiusKm) : RecordArea;

/// <summary>Within a rectangle of latitude and longitude, west never east of east.</summary>
public sealed record BoxRecordArea(double West, double South, double East, double North) : RecordArea;

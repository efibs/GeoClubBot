namespace UseCases.OutputPorts.AI;

/// <summary>
/// Draws where a plant species is recorded as a map, and keeps the picture where an answer can attach it.
///
/// A list of countries with record counts answers "where does it grow" in words; members asked for the
/// map — "Please show the GBIF distribution", "I would have liked a distribution map" (2026-11-09). For
/// a game played on a map, the picture is the answer: where in the United States the cholla grows is a
/// shape, not five state names.
/// </summary>
public interface ISpeciesRangeMap
{
    /// <summary>
    /// The link the map of <paramref name="range"/> is served under — the world's part that holds its
    /// records, or the one country it was counted in — or null when no map could be drawn: the database
    /// or its map tiles out of reach, nowhere to keep the picture, or the feature off. Never throws for
    /// any of those; the range is then offered in words alone.
    /// </summary>
    Task<string?> DrawAsync(SpeciesRange range, CancellationToken cancellationToken = default);
}

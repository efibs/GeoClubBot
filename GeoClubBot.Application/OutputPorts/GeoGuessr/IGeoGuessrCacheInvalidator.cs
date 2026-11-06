namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// Forgets cached GeoGuessr reads that a push from GeoGuessr's socket has just made stale, so the
/// next read fetches the new state instead of serving the old one for the rest of its time to live.
/// </summary>
public interface IGeoGuessrCacheInvalidator
{
    /// <summary>The club's current mission board (<see cref="IClubMissionBoardReader.ReadCurrentAsync"/>).</summary>
    void InvalidateCurrentMissionBoard(Guid clubId);

    /// <summary>The club's level, member count and stats (<see cref="IGeoGuessrClubReader"/>, the website's figures).</summary>
    void InvalidateClub(Guid clubId);
}

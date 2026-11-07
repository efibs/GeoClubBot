using Microsoft.Extensions.Caching.Memory;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

/// <summary>
/// Removes entries of <see cref="CachingClubMissionBoardReader"/> and <see cref="CachingGeoGuessrClubReader"/>,
/// by the keys those readers define.
/// </summary>
public sealed class GeoGuessrCacheInvalidator(IMemoryCache cache) : IGeoGuessrCacheInvalidator
{
    // The previous board is left alone: it can only change in the week it was current.
    public void InvalidateCurrentMissionBoard(Guid clubId) =>
        cache.Remove(CachingClubMissionBoardReader.CacheKey(clubId, CachingClubMissionBoardReader.Current));

    public void InvalidateClub(Guid clubId) =>
        cache.Remove(CachingGeoGuessrClubReader.CacheKey(clubId));
}

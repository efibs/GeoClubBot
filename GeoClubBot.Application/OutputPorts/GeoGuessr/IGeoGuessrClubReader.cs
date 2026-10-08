namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// Reads a club's public figures live from GeoGuessr, cached for a while because every visit of the
/// club website asks for them.
/// </summary>
public interface IGeoGuessrClubReader
{
    /// <summary>
    /// The club's figures; null when GeoGuessr could not be read. A failure is not cached, so the
    /// next call tries again.
    /// </summary>
    Task<GeoGuessrClubSnapshot?> ReadClubAsync(Guid clubId, CancellationToken cancellationToken = default);
}

/// <summary>A club's figures as GeoGuessr's club endpoint reported them.</summary>
/// <param name="TotalXp">The club's total XP (<c>stats.totalXp</c>).</param>
/// <param name="GlobalXpRank">Position on the club leaderboard; 1 is first place.</param>
/// <param name="TotalClubs">Number of clubs on the leaderboard.</param>
/// <param name="ReadAt">When GeoGuessr was asked, not when the copy was served from the cache.</param>
public sealed record GeoGuessrClubSnapshot(
    Guid ClubId,
    int Level,
    int MemberCount,
    int TotalXp,
    int GlobalXpRank,
    int TotalClubs,
    DateTimeOffset ReadAt);

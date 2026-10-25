using Entities;

namespace GeoClubBot.Tests.TestBuilders;

/// <summary>Builds recorded mission claims for tests; the board week defaults to one still running.</summary>
public static class MissionClaims
{
    public static MissionClaim Completed(
        string userId,
        DateTimeOffset claimedAt,
        TimeSpan took,
        Guid? clubId = null,
        DateTimeOffset? periodEnd = null) =>
        MissionClaim.Create(
            clubId ?? Guid.Empty, claimedAt.AddDays(-1), periodEnd ?? claimedAt.AddDays(6),
            Guid.NewGuid(), userId, claimedAt, claimedAt + took);

    /// <summary>A claim not completed; abandoned once <paramref name="periodEnd"/> has passed.</summary>
    public static MissionClaim Unfinished(
        string userId,
        DateTimeOffset claimedAt,
        DateTimeOffset periodEnd,
        Guid? clubId = null) =>
        MissionClaim.Create(
            clubId ?? Guid.Empty, claimedAt.AddDays(-1), periodEnd,
            Guid.NewGuid(), userId, claimedAt, completedAt: null);
}

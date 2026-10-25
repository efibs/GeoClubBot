using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IMissionClaimRepository
{
    /// <summary>The recorded claims of one board week of <paramref name="clubId"/>, for tracking.</summary>
    Task<List<MissionClaim>> ReadForUpdateByPeriodAsync(
        Guid clubId,
        DateTimeOffset periodStart,
        CancellationToken cancellationToken = default);

    /// <summary>Claims of <paramref name="userId"/> made between <paramref name="from"/> and <paramref name="to"/>.</summary>
    Task<List<MissionClaim>> ReadClaimsOfUserAsync(
        string userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>Claims made in <paramref name="clubId"/> between <paramref name="from"/> and <paramref name="to"/>.</summary>
    Task<List<MissionClaim>> ReadClaimsOfClubAsync(
        Guid clubId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    void Add(MissionClaim claim);

    /// <summary>Deletes claims made before <paramref name="threshold"/>.</summary>
    Task<int> DeleteClaimedBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}

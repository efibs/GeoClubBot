using Entities;

namespace UseCases.OutputPorts.Repositories;

/// <summary>A subscriber of a club's board, with the GeoGuessr account it is linked to.</summary>
public sealed record MissionBoardSubscriber(ulong DiscordUserId, string GeoGuessrUserId, MissionBoardSubscriptionMode Mode);

public interface IMissionBoardSubscriptionRepository
{
    /// <summary>The member's subscription, tracked for changes; null when they have none.</summary>
    Task<MissionBoardSubscription?> ReadForUpdateAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    /// <summary>The subscribers whose linked GeoGuessr account is currently a member of <paramref name="clubId"/>.</summary>
    Task<List<MissionBoardSubscriber>> ReadSubscribersOfClubAsync(Guid clubId, CancellationToken cancellationToken = default);

    void Add(MissionBoardSubscription subscription);

    /// <summary>Deletes the member's subscription at once; whether there was one.</summary>
    Task<bool> DeleteAsync(ulong discordUserId, CancellationToken cancellationToken = default);
}

using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IGeoGuessrUserRepository
{
    void AddUser(GeoGuessrUser user);

    Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<GeoGuessrUser?> ReadForUpdateByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<GeoGuessrUser?> ReadUserByDiscordUserIdAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    Task<GeoGuessrUser?> ReadForUpdateByDiscordUserIdAsync(ulong discordUserId, CancellationToken cancellationToken = default);

    Task<List<GeoGuessrUser>> ReadAllLinkedUsersAsync(CancellationToken cancellationToken = default);
}

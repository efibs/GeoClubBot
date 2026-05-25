using Entities;

namespace UseCases.OutputPorts;

public interface IGeoGuessrUserRepository
{
    void AddUser(GeoGuessrUser user);

    Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId);

    Task<GeoGuessrUser?> ReadForUpdateByUserIdAsync(string userId);

    Task<GeoGuessrUser?> ReadUserByDiscordUserIdAsync(ulong discordUserId);

    Task<GeoGuessrUser?> ReadForUpdateByDiscordUserIdAsync(ulong discordUserId);

    Task<List<GeoGuessrUser>> ReadAllLinkedUsersAsync();
}

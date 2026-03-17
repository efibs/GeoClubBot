using Entities;

namespace UseCases.OutputPorts;

public interface IGeoGuessrUserRepository
{
    GeoGuessrUser CreateUser(GeoGuessrUser user);
    
    Task<GeoGuessrUser?> UpdateUserAsync(GeoGuessrUser user);
    
    Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId);

    Task<GeoGuessrUser?> ReadUserByDiscordUserIdAsync(ulong discordUserId);
    
    Task<List<GeoGuessrUser>> ReadAllLinkedUsersAsync();

    Task<GeoGuessrUser?> LinkDiscordAccountAsync(string userId, ulong discordUserId);

    Task<GeoGuessrUser?> UnlinkDiscordAccountAsync(string userId);
}
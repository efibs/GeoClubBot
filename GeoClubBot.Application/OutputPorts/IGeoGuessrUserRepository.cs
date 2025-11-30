using Entities;

namespace UseCases.OutputPorts;

public interface IGeoGuessrUserRepository
{
    GeoGuessrUser CreateUser(GeoGuessrUser user);
    
    GeoGuessrUser UpdateUser(GeoGuessrUser user);
    
    Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId);

    Task<GeoGuessrUser?> ReadUserByDiscordUserIdAsync(ulong discordUserId);
    
    Task<List<GeoGuessrUser>> ReadAllLinkedUsersAsync();
}
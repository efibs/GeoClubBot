using Entities;

namespace UseCases.OutputPorts;

public interface IGeoGuessrUserRepository
{
    Task<GeoGuessrUser> CreateUserAsync(GeoGuessrUser user);
    
    Task<GeoGuessrUser> UpdateUserAsync(GeoGuessrUser user);
    
    Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId);

    Task<GeoGuessrUser?> ReadUserByDiscordUserIdAsync(ulong discordUserId);
    
    Task<List<GeoGuessrUser>> ReadAllLinkedUsersAsync();
}
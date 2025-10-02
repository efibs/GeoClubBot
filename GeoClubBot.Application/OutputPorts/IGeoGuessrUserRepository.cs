using Entities;

namespace UseCases.OutputPorts;

public interface IGeoGuessrUserRepository
{
    Task<GeoGuessrUser> CreateOrUpdateUserAsync(GeoGuessrUser user);
    
    Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId);

    Task<GeoGuessrUser?> ReadUserByDiscordUserIdAsync(ulong discordUserId);
    
    Task<List<GeoGuessrUser>> ReadAllLinkedUsersAsync();
}
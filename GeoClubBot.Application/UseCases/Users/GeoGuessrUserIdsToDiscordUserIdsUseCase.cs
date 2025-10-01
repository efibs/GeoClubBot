using UseCases.InputPorts.Organization;
using UseCases.InputPorts.Users;

namespace UseCases.UseCases.Users;

public class GeoGuessrUserIdsToDiscordUserIdsUseCase(IReadOrSyncGeoGuessrUserUseCase readOrSyncGeoGuessrUserUseCase) : IGeoGuessrUserIdsToDiscordUserIdsUseCase
{
    public async Task<List<ulong>> GetDiscordUserIdsAsync(IEnumerable<string> geoGuessrUserIds)
    {
        // Create a new list
        var discordUserIds = new List<ulong>();

        // For every GeoGuessr user id
        foreach (var geoGuessrUserId in geoGuessrUserIds)
        {
            // Try to read the user
            var geoGuessrUser =
                await readOrSyncGeoGuessrUserUseCase.ReadOrSyncGeoGuessrUserByUserIdAsync(geoGuessrUserId).ConfigureAwait(false);
            
            // If there is a discord user id set
            if (geoGuessrUser?.DiscordUserId != null)
            {
                // Add the discord user id to the list
                discordUserIds.Add(geoGuessrUser.DiscordUserId.Value);
            }
        }
        
        return discordUserIds;
    }
}
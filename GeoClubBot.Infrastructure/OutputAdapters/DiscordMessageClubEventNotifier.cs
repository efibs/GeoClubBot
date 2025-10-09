using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DiscordMessageClubEventNotifier(IMessageAccess messageAccess, IConfiguration config) : IClubEventNotifier
{
    public async Task SendClubLevelUpEvent(Club club)
    {
        // Build the message
        var message = $"{club.Name} is now level {club.Level} in GeoGuessr! :partying_face: ";
        
        // Send the message
        await messageAccess.SendMessageAsync(message, _levelUpMessageChannelId).ConfigureAwait(false);
    }
    
    private readonly string _levelUpMessageChannelId = config.GetValue<string>(ConfigKeys.ClubLevelCheckerLevelUpMessageChannelIdConfigurationKey)!;
}
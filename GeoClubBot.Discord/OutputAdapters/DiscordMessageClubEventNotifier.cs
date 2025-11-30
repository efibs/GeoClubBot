using Configuration;
using Entities;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Discord.OutputAdapters;

public class DiscordMessageClubEventNotifier(IDiscordMessageAccess discordMessageAccess, IOptions<ClubLevelCheckerConfiguration> config) : IClubEventNotifier
{
    public async Task SendClubLevelUpEvent(Club club)
    {
        // Build the message
        var message = $"{club.Name} is now level {club.Level} in GeoGuessr! :partying_face: ";
        
        // Send the message
        await discordMessageAccess.SendMessageAsync(message, config.Value.LevelUpMessageChannelId).ConfigureAwait(false);
    }
}
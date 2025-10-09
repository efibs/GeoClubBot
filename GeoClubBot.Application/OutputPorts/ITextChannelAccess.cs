using Entities;

namespace UseCases.OutputPorts;

public interface ITextChannelAccess
{
    Task<ulong?> CreatePrivateTextChannelAsync(ulong categoryId, 
        string name, 
        string description, 
        IEnumerable<ulong>? allowedDiscordUserIds,
        IEnumerable<ulong>? allowedRoleIds);

    Task UpdateTextChannelAsync(TextChannel newTextChannel);
    
    Task<bool> DeleteTextChannelAsync(ulong textChannelId);
    
    Task<ulong?> ReadLastMessageOfUserAsync(ulong userId, 
        ulong channelId, 
        int numMessageSearchlimit);
}
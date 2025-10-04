using Discord;

namespace UseCases.OutputPorts;

public interface ITextChannelAccess
{
    Task<ulong?> CreatePrivateTextChannelAsync(ulong categoryId, 
        string name, 
        string description, 
        IEnumerable<ulong>? allowedDiscordUserIds,
        IEnumerable<ulong>? allowedRoleIds);

    Task UpdateTextChannelAsync(ulong textChannelId, Action<TextChannelProperties> updateAction);
    
    Task<bool> DeleteTextChannelAsync(ulong textChannelId);
}
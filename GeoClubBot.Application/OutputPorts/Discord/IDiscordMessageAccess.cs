using Entities;

namespace UseCases.OutputPorts.Discord;

public interface IDiscordMessageAccess
{
    Task SendMessageAsync(string message, ulong channelId);

    Task SendSelfRolesMessageAsync(ulong channelId, 
        IEnumerable<SelfRoleSetting> selfRoleSettings);
    
    Task UpdateSelfRolesMessageAsync(ulong channelId, ulong messageId, 
        IEnumerable<SelfRoleSetting> selfRoleSettings);
    
    Task DeleteMessageAsync(ulong messageId, ulong channelId);
}
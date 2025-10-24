using Entities;

namespace UseCases.OutputPorts;

public interface IMessageAccess
{
    Task SendMessageAsync(string message, string channelId);

    Task SendSelfRolesMessageAsync(ulong channelId, 
        IEnumerable<SelfRoleSetting> selfRoleSettings);
    
    Task UpdateSelfRolesMessageAsync(ulong channelId, ulong messageId, 
        IEnumerable<SelfRoleSetting> selfRoleSettings);
    
    Task DeleteMessageAsync(ulong messageId, ulong channelId);
}
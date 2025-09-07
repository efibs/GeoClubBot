namespace UseCases.OutputPorts;

public interface IServerRolesAccess
{
    Task<int> RemoveRoleFromAllPlayersAsync(ulong roleId);
    
    Task RemoveRolesFromUserAsync(ulong userId, IEnumerable<ulong> roleIds);
    
    Task AddRoleToMembersByUserIdsAsync(IEnumerable<ulong> userIds, ulong roleId);
}
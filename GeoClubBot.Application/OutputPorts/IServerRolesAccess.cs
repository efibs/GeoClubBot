namespace UseCases.OutputPorts;

public interface IServerRolesAccess
{
    Task<int> RemoveRoleFromAllPlayersAsync(ulong roleId);
    
    Task RemoveRolesFromUserAsync(ulong userId, IEnumerable<ulong> roleIds);
    
    Task RemoveRoleFromPlayersAsync(IEnumerable<ulong> userIds, ulong roleId);
    
    Task AddRoleToMembersByUserIdsAsync(IEnumerable<ulong> userIds, ulong roleId);

    Task<List<ulong>> ReadMembersWithRoleAsync(ulong roleId);
}
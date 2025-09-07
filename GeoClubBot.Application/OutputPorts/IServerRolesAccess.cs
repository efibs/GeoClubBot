namespace UseCases.OutputPorts;

public interface IServerRolesAccess
{
    Task<int> RemoveRoleFromAllPlayersAsync(ulong roleId);
    
    Task AddRoleToMembersByUserIdsAsync(IEnumerable<ulong> userIds, ulong roleId);
}
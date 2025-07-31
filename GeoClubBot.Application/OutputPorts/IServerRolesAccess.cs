namespace UseCases.OutputPorts;

public interface IServerRolesAccess
{
    Task<int> RemoveRoleFromAllPlayersAsync(ulong roleId);
    
    Task AddRoleToMembersByNicknameAsync(HashSet<string> nicknames, ulong roleId);
}
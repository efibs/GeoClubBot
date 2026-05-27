namespace UseCases.OutputPorts.Discord;

public interface IDiscordServerRolesAccess
{
    Task<int> RemoveRoleFromAllPlayersAsync(ulong roleId, CancellationToken cancellationToken = default);

    Task RemoveRolesFromUserAsync(ulong userId, IEnumerable<ulong> roleIds, CancellationToken cancellationToken = default);

    Task RemoveRoleFromPlayersAsync(IEnumerable<ulong> userIds, ulong roleId, CancellationToken cancellationToken = default);

    Task AddRoleToMembersByUserIdsAsync(IEnumerable<ulong> userIds, ulong roleId, CancellationToken cancellationToken = default);

    Task<List<ulong>> ReadMembersWithRoleAsync(ulong roleId, CancellationToken cancellationToken = default);
}

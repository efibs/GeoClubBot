using Entities;

namespace UseCases.OutputPorts.Discord;

public interface IDiscordMessageAccess
{
    Task SendMessageAsync(string message, ulong channelId, CancellationToken cancellationToken = default);

    Task SendSelfRolesMessageAsync(ulong channelId,
        IEnumerable<SelfRoleSetting> selfRoleSettings,
        CancellationToken cancellationToken = default);

    Task UpdateSelfRolesMessageAsync(ulong channelId, ulong messageId,
        IEnumerable<SelfRoleSetting> selfRoleSettings,
        CancellationToken cancellationToken = default);

    Task DeleteMessageAsync(ulong messageId, ulong channelId, CancellationToken cancellationToken = default);
}

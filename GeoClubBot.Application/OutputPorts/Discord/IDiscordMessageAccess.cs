using Entities;

namespace UseCases.OutputPorts.Discord;

public interface IDiscordMessageAccess
{
    Task SendMessageAsync(string message, ulong channelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message that may ping exactly the targets in <paramref name="allowedMentions"/>. Messages
    /// longer than Discord's limit are split at line breaks.
    /// </summary>
    /// <returns>The id of the last message sent.</returns>
    Task<ulong> SendMessageAsync(
        string message,
        ulong channelId,
        MessageMentions allowedMentions,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a public thread on a message.</summary>
    Task CreateThreadAsync(
        ulong channelId,
        ulong messageId,
        string name,
        ThreadAutoArchive autoArchive,
        CancellationToken cancellationToken = default);

    Task SendSelfRolesMessageAsync(ulong channelId,
        IEnumerable<SelfRoleSetting> selfRoleSettings,
        CancellationToken cancellationToken = default);

    Task UpdateSelfRolesMessageAsync(ulong channelId, ulong messageId,
        IEnumerable<SelfRoleSetting> selfRoleSettings,
        CancellationToken cancellationToken = default);

    Task DeleteMessageAsync(ulong messageId, ulong channelId, CancellationToken cancellationToken = default);
}

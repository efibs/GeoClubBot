using Entities;

namespace UseCases.OutputPorts.Discord;

public interface IDiscordTextChannelAccess
{
    Task<ulong?> CreatePrivateTextChannelAsync(ulong categoryId,
        string name,
        string description,
        IEnumerable<ulong>? allowedDiscordUserIds,
        IEnumerable<ulong>? allowedRoleIds,
        CancellationToken cancellationToken = default);

    Task UpdateTextChannelAsync(TextChannel newTextChannel, CancellationToken cancellationToken = default);

    Task<bool> DeleteTextChannelAsync(ulong textChannelId, CancellationToken cancellationToken = default);

    Task<ulong?> ReadLastMessageOfUserAsync(ulong userId,
        ulong channelId,
        int numMessageSearchlimit,
        CancellationToken cancellationToken = default);
}

using Entities;

namespace UseCases.OutputPorts.Discord;

public interface IDiscordTextChannelAccess
{
    /// <summary>
    /// Creates a text channel in <paramref name="categoryId"/> whose permission overwrites are exactly
    /// <paramref name="access"/>; the channel is not synced to its category.
    /// </summary>
    Task<ulong?> CreatePrivateTextChannelAsync(ulong categoryId,
        string name,
        string description,
        IReadOnlyCollection<ChannelAccess> access,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a category whose permission overwrites are exactly <paramref name="access"/>.</summary>
    Task<ulong?> CreateCategoryAsync(string name,
        IReadOnlyCollection<ChannelAccess> access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every channel of the server through REST, so a channel created or moved a moment ago is
    /// already counted where it is now.
    /// </summary>
    Task<GuildChannelLayout> ReadChannelLayoutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the permission overwrites of a channel or category with exactly <paramref name="access"/>.
    /// Returns false when the channel no longer exists.
    /// </summary>
    Task<bool> SetChannelAccessAsync(ulong channelId,
        IReadOnlyCollection<ChannelAccess> access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the non-null parts of <paramref name="newTextChannel"/> (name, description, category)
    /// to the existing channel. Returns false when the channel no longer exists.
    /// </summary>
    Task<bool> UpdateTextChannelAsync(TextChannel newTextChannel, CancellationToken cancellationToken = default);

    Task<bool> DeleteTextChannelAsync(ulong textChannelId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a category. Returns false when it no longer exists.</summary>
    Task<bool> DeleteCategoryAsync(ulong categoryId, CancellationToken cancellationToken = default);

    Task<ulong?> ReadLastMessageOfUserAsync(ulong userId,
        ulong channelId,
        int numMessageSearchlimit,
        CancellationToken cancellationToken = default);
}

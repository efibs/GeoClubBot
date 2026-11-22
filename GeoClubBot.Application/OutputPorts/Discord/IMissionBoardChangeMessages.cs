using Utilities;

namespace UseCases.OutputPorts.Discord;

/// <summary>The DMs of the mission board change notifications (#504).</summary>
public interface IMissionBoardChangeMessages
{
    /// <summary>
    /// DMs <paramref name="text"/>, with an unsubscribe button when <paramref name="withUnsubscribeButton"/>.
    /// Fails like <see cref="IDiscordDirectMessageAccess.SendDirectMessageAsync"/>.
    /// </summary>
    Task<Result> SendAsync(ulong discordUserId, string text, bool withUnsubscribeButton, CancellationToken cancellationToken = default);
}

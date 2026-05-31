using Utilities;

namespace UseCases.OutputPorts.Discord;

public interface IDiscordDirectMessageAccess
{
    /// <summary>
    /// Attempts to deliver a direct message to the given Discord user.
    /// </summary>
    /// <returns>
    /// <see cref="Result.Success"/> when the DM was delivered; a <see cref="ErrorType.Forbidden"/>
    /// error (code <c>discord.dm.disabled</c>) when the user does not accept DMs from the bot or has
    /// blocked it — a permanent condition the user must fix; otherwise a <see cref="ErrorType.Unexpected"/>
    /// error (code <c>discord.dm.failed</c>) for transient or unexpected failures (network, rate limits,
    /// Discord outages) that may succeed on a later attempt.
    /// </returns>
    Task<Result> SendDirectMessageAsync(ulong discordUserId, string message, CancellationToken cancellationToken = default);
}

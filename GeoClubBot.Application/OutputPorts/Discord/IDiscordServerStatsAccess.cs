namespace UseCases.OutputPorts.Discord;

public interface IDiscordServerStatsAccess
{
    /// <summary>
    /// Discord's approximate count of the server's members who are online right now — the "N Online"
    /// of an invite. Asks Discord every time; throws when Discord cannot answer.
    /// </summary>
    Task<int> ReadApproximateOnlineCountAsync(CancellationToken cancellationToken = default);
}

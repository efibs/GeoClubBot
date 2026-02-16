namespace UseCases.OutputPorts.Discord;

public interface IDiscordDirectMessageAccess
{
    Task<bool> SendDirectMessageAsync(ulong discordUserId, string message);
}

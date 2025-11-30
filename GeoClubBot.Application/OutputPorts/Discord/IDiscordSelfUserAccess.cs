namespace UseCases.OutputPorts.Discord;

public interface IDiscordSelfUserAccess
{
    ulong GetSelfUserId();
}
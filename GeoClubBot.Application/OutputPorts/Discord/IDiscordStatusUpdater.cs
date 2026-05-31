namespace UseCases.OutputPorts.Discord;

public interface IDiscordStatusUpdater
{
    Task UpdateStatusAsync(string newStatus, CancellationToken cancellationToken = default);
}

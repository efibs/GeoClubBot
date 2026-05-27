namespace UseCases.OutputPorts;

public interface IDiscordStatusUpdater
{
    Task UpdateStatusAsync(string newStatus, CancellationToken cancellationToken = default);
}

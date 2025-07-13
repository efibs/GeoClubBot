namespace UseCases.OutputPorts;

public interface IStatusUpdater
{
    Task UpdateStatusAsync(string newStatus);
}
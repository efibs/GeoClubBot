namespace UseCases.InputPorts;

public interface IGetLastCheckTimeUseCase
{
    Task<DateTimeOffset?> GetLastCheckTimeAsync();
}
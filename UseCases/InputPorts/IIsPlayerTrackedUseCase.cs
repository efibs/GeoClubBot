namespace UseCases.InputPorts;

public interface IIsPlayerTrackedUseCase
{
    Task<bool> IsPlayerTrackedAsync(string memberNickname);
}
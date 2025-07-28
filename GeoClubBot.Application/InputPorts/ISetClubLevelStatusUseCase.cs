namespace UseCases.InputPorts;

public interface ISetClubLevelStatusUseCase
{
    Task SetClubLevelStatusAsync(int level);
}
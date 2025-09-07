namespace UseCases.InputPorts.Club;

public interface ISetClubLevelStatusUseCase
{
    Task SetClubLevelStatusAsync(int level);
}
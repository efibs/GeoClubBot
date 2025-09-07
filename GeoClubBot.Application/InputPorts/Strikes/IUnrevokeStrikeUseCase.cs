namespace UseCases.InputPorts.Strikes;

public interface IUnrevokeStrikeUseCase
{
    Task<bool> UnrevokeStrikeAsync(Guid strikeId);
}
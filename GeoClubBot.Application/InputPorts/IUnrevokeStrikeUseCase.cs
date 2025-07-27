namespace UseCases.InputPorts;

public interface IUnrevokeStrikeUseCase
{
    Task<bool> UnrevokeStrikeAsync(Guid strikeId);
}
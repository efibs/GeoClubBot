namespace UseCases.InputPorts.Strikes;

public interface IRevokeStrikeUseCase
{
    Task<bool> RevokeStrikeAsync(Guid strikeId);
}
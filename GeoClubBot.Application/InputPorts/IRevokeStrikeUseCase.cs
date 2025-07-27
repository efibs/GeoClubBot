namespace UseCases.InputPorts;

public interface IRevokeStrikeUseCase
{
    Task<bool> RevokeStrikeAsync(Guid strikeId);
}
namespace UseCases.InputPorts;

public interface IRemoveExcuseUseCase
{
    Task<bool> RemoveExcuseAsync(Guid excuseId);
}
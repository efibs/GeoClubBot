namespace UseCases.InputPorts.Excuses;

public interface IRemoveExcuseUseCase
{
    Task<bool> RemoveExcuseAsync(Guid excuseId);
}
namespace UseCases.InputPorts;

public interface IAddExcuseUseCase
{
    Task<Guid> AddExcuseAsync(string memberNickname, DateTimeOffset from, DateTimeOffset to);
}
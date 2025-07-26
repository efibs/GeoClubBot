namespace UseCases.InputPorts;

public interface IReadMemberNumStrikesUseCase
{
    Task<int?> ReadMemberNumStrikesAsync(string memberNickname);
}
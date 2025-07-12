namespace UseCases.InputPorts;

public interface IWriteMemberNumStrikesUseCase
{
    Task<bool> WriteNumStrikesAsync(string memberNickname, int numStrikes);
}
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class ReadMemberNumStrikesUseCase(IStrikesRepository strikesRepository) : IReadMemberNumStrikesUseCase
{
    public async Task<int?> ReadMemberNumStrikesAsync(string memberNickname)
    {
        // Read the number of strikes
        var strikes = await strikesRepository.ReadNumberOfStrikesByMemberNicknameAsync(memberNickname);

        return strikes;
    }
}
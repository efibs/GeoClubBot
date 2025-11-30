namespace UseCases.InputPorts.ClubMemberActivity;

public interface IRenderHistoryUseCase
{
    MemoryStream RenderHistory(List<int> values, List<DateTimeOffset> timestamps, int target);
}
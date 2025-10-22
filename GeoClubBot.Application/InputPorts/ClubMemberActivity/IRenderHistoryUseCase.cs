using Entities;

namespace UseCases.InputPorts.ClubMemberActivity;

public interface IRenderHistoryUseCase
{
    MemoryStream RenderHistory(List<HistoryEntry> history, int target);
}
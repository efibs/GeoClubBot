namespace UseCases.InputPorts.ClubMemberActivity;

public interface IRenderPlayerActivityUseCase
{
    Task<MemoryStream?> RenderPlayerActivityAsync(string nickname, int maxNumHistoryEntries);
}
using Entities;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMemberActivity;

public class RenderPlayerActivityUseCase(IHistoryRepository repository, IRenderHistoryUseCase renderHistoryUseCase) : IRenderPlayerActivityUseCase
{
    public async Task<MemoryStream?> RenderPlayerActivityAsync(string nickname, int maxNumHistoryEntries)
    {
        // Get the relevant history entries
        var playersHistoryEntries = await repository
            .ReadHistoryEntriesByPlayerNicknameAsync(nickname)
            .ConfigureAwait(false);
        
        // If the player does not exist
        if (playersHistoryEntries == null || playersHistoryEntries.Count < 3)
        {
            return null;
        }
        
        // Order by timestamp
        var entriesOrdered = playersHistoryEntries
            .OrderByDescending(e => e.Timestamp)
            .ToList();
        
        // zip and take max num entries
        var playerHistory = entriesOrdered
            .Zip(entriesOrdered.Skip(1))
            .Select(e => new HistoryEntry(e.Second.Timestamp, e.First.Xp - e.Second.Xp))
            .Take(maxNumHistoryEntries)
            .ToList();
        
        // Create plot
        var plotStream = renderHistoryUseCase.RenderHistory(playerHistory);
        
        return plotStream;
    }
}
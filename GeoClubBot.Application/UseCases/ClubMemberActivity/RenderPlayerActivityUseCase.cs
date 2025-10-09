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
            .OrderBy(e => e.Timestamp)
            .ToList();
        
        // Get the first entry
        var firstHistoryEntry = entriesOrdered.First();
        
        // zip and take max num entries
        var playerHistory = entriesOrdered
            .Prepend(new ClubMemberHistoryEntry {Timestamp = firstHistoryEntry.Timestamp, UserId = firstHistoryEntry.UserId, Xp = 0})
            .Zip(entriesOrdered)
            .Select(e => new HistoryEntry(e.Second.Timestamp, e.Second.Xp - e.First.Xp))
            .Take(maxNumHistoryEntries + 1)
            .ToList();
        
        // Create plot
        var plotStream = renderHistoryUseCase.RenderHistory(playerHistory);
        
        return plotStream;
    }
}
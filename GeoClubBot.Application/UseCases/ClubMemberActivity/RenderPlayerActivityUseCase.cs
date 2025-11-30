using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.ClubMemberActivity;
using UseCases.OutputPorts;

namespace UseCases.UseCases.ClubMemberActivity;

public class RenderPlayerActivityUseCase(IUnitOfWork unitOfWork, 
    IRenderHistoryUseCase renderHistoryUseCase, 
    IConfiguration config) : IRenderPlayerActivityUseCase
{
    public async Task<MemoryStream?> RenderPlayerActivityAsync(string nickname, int maxNumHistoryEntries)
    {
        // Get the relevant history entries
        var playersHistoryEntries = await unitOfWork.History
            .ReadHistoryEntriesByPlayerNicknameAsync(nickname)
            .ConfigureAwait(false);
        
        // If there are no entries
        if (playersHistoryEntries == null)
        {
            return null;
        }
        
        // Read the member
        var member = await unitOfWork.ClubMembers
            .ReadClubMemberByNicknameAsync(nickname)
            .ConfigureAwait(false);
        
        // If the club member was found
        if (member != null)
        {
            // Prepend 0 at joined date
            playersHistoryEntries = playersHistoryEntries.Prepend(new ClubMemberHistoryEntry
            {
                UserId = member.UserId,
                Timestamp = member.JoinedAt,
                Xp = 0
            }).ToList();
        }

        // If the player doesn't have enough entries
        if (playersHistoryEntries.Count < 2)
        {
            return null;
        }
        
        // Order by timestamp
        var entriesOrdered = playersHistoryEntries
            .OrderBy(e => e.Timestamp);

        // Take only the requested number of entries (from the back)
        var entriesToShow = entriesOrdered
            .Skip(playersHistoryEntries.Count - maxNumHistoryEntries - 1)
            .ToList();
        
        // Zip to calculate the differences
        var values = entriesToShow
            .Skip(1)
            .Zip(entriesToShow, (a, b) => 
                a.Xp - b.Xp)
            .ToList();
        
        // Get the timestamps
        var timestamps = entriesToShow.Select(e => e.Timestamp).ToList();
        
        // Create plot
        var plotStream = renderHistoryUseCase.RenderHistory(values, timestamps, _weeklyXpTarget);
        
        return plotStream;
    }
    
    private readonly int _weeklyXpTarget = config.GetValue<int>(ConfigKeys.ActivityCheckerMinXpConfigurationKey);
}
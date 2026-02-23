using Entities;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.ClubMembers;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class DeleteMemberPrivateChannelUseCase(IDiscordTextChannelAccess discordTextChannelAccess,
    ICreateOrUpdateClubMemberUseCase createOrUpdateClubMemberUseCase,
    ILogger<DeleteMemberPrivateChannelUseCase> logger) : IDeleteMemberPrivateChannelUseCase
{
    public async Task<bool> DeletePrivateChannelAsync(ClubMember? clubMember)
    {
        // Log info
        LogDeletingPrivateChannel(logger, clubMember?.User?.Nickname ?? "<null>");
        
        // If the user had no text channel set
        if (clubMember?.PrivateTextChannelId == null)
        {
            return false;
        }
        
        // Try to delete the text channel
        var successful = await discordTextChannelAccess.DeleteTextChannelAsync(clubMember.PrivateTextChannelId.Value).ConfigureAwait(false);
            
        // remove the text channel id from the club member
        clubMember.PrivateTextChannelId = null;
            
        // Save the club member
        await createOrUpdateClubMemberUseCase.CreateOrUpdateClubMemberAsync(clubMember).ConfigureAwait(false);
        
        return successful;
    }
    
    [LoggerMessage(LogLevel.Information, "Deleting private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogDeletingPrivateChannel(ILogger<DeleteMemberPrivateChannelUseCase> logger, string clubMemberNickname);
}
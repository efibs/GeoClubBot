using Entities;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class DeleteMemberPrivateChannelUseCase(IDiscordTextChannelAccess discordTextChannelAccess,
    IUnitOfWork unitOfWork,
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

        // Clear the private text channel id from the club member
        await unitOfWork.ClubMembers.ClearPrivateTextChannelIdAsync(clubMember.UserId).ConfigureAwait(false);

        return successful;
    }
    
    [LoggerMessage(LogLevel.Information, "Deleting private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogDeletingPrivateChannel(ILogger<DeleteMemberPrivateChannelUseCase> logger, string clubMemberNickname);
}
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.MemberPrivateChannels;

public sealed record DeleteMemberPrivateChannelCommand(ClubMember? ClubMember) : ICommand<bool>;

public sealed partial class DeleteMemberPrivateChannelHandler(
    IDiscordTextChannelAccess discordTextChannelAccess,
    IClubMemberRepository clubMembers,
    ILogger<DeleteMemberPrivateChannelHandler> logger)
    : IRequestHandler<DeleteMemberPrivateChannelCommand, bool>
{
    public async Task<bool> Handle(DeleteMemberPrivateChannelCommand request, CancellationToken cancellationToken)
    {
        var clubMember = request.ClubMember;
        LogDeletingPrivateChannel(logger, clubMember?.User?.Nickname ?? "<null>");

        if (clubMember?.PrivateTextChannelId is null)
        {
            return false;
        }

        var successful = await discordTextChannelAccess
            .DeleteTextChannelAsync(clubMember.PrivateTextChannelId.Value, cancellationToken)
            .ConfigureAwait(false);

        var trackedMember = await clubMembers
            .ReadForUpdateByUserIdAsync(clubMember.UserId, cancellationToken)
            .ConfigureAwait(false);
        trackedMember?.SetPrivateTextChannelId(null);

        return successful;
    }

    [LoggerMessage(LogLevel.Information, "Deleting private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogDeletingPrivateChannel(ILogger<DeleteMemberPrivateChannelHandler> logger, string clubMemberNickname);
}

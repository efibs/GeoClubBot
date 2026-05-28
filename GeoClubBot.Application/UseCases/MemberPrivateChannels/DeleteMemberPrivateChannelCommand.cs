using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using Utilities;

namespace UseCases.UseCases.MemberPrivateChannels;

public sealed record DeleteMemberPrivateChannelCommand(ClubMember? ClubMember) : ICommand<Result>;

public sealed partial class DeleteMemberPrivateChannelHandler(
    IDiscordTextChannelAccess discordTextChannelAccess,
    IClubMemberRepository clubMembers,
    ILogger<DeleteMemberPrivateChannelHandler> logger)
    : IRequestHandler<DeleteMemberPrivateChannelCommand, Result>
{
    public async Task<Result> Handle(DeleteMemberPrivateChannelCommand request, CancellationToken cancellationToken)
    {
        var clubMember = request.ClubMember;
        LogDeletingPrivateChannel(logger, clubMember?.User?.Nickname ?? "<null>");

        if (clubMember?.PrivateTextChannelId is null)
        {
            return Error.NotFound(
                "member_private_channel.not_found",
                "No private text channel is configured for the given club member.");
        }

        var successful = await discordTextChannelAccess
            .DeleteTextChannelAsync(clubMember.PrivateTextChannelId.Value, cancellationToken)
            .ConfigureAwait(false);

        var trackedMember = await clubMembers
            .ReadForUpdateByUserIdAsync(clubMember.UserId, cancellationToken)
            .ConfigureAwait(false);
        trackedMember?.SetPrivateTextChannelId(null);

        if (!successful)
        {
            return Error.Unexpected(
                "member_private_channel.delete_failed",
                "The Discord channel deletion call did not succeed.");
        }

        return Result.Success();
    }

    [LoggerMessage(LogLevel.Information, "Deleting private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogDeletingPrivateChannel(ILogger<DeleteMemberPrivateChannelHandler> logger, string clubMemberNickname);
}

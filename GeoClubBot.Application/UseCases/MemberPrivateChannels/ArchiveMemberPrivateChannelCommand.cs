using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using Utilities;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// Moves a member's private channel into the archive category instead of deleting it, so admins can
/// still talk to someone who was just kicked and a hop to the second club keeps its history. A full
/// archive makes room by deleting the channel archived the longest.
/// </summary>
public sealed record ArchiveMemberPrivateChannelCommand(ClubMember? ClubMember) : ICommand<Result>;

public sealed partial class ArchiveMemberPrivateChannelHandler(
    MemberPrivateChannelManager manager,
    ILogger<ArchiveMemberPrivateChannelHandler> logger)
    : IRequestHandler<ArchiveMemberPrivateChannelCommand, Result>
{
    public async Task<Result> Handle(ArchiveMemberPrivateChannelCommand request, CancellationToken cancellationToken)
    {
        var clubMember = request.ClubMember;

        if (clubMember?.PrivateTextChannelId is null)
        {
            return Error.NotFound(
                "member_private_channel.not_found",
                "No private text channel is configured for the given club member.");
        }

        LogArchivingPrivateChannel(logger, clubMember.User.Nickname);

        var result = await PrivateChannelPlacementLock.RunAsync(async () =>
        {
            var placement = await manager.LoadAsync(cancellationToken).ConfigureAwait(false);
            var archived = await manager.ArchiveAsync(placement, clubMember, cancellationToken).ConfigureAwait(false);

            if (archived.IsSuccess)
            {
                // The member's old club category may be an overflow one that is empty now.
                await manager.PruneEmptyCategoriesAsync(placement, cancellationToken).ConfigureAwait(false);
            }

            return archived;
        }, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            LogPrivateChannelArchived(logger, clubMember.User.Nickname, clubMember.PrivateTextChannelId.Value);
        }

        return result;
    }

    [LoggerMessage(LogLevel.Information, "Archiving private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogArchivingPrivateChannel(ILogger<ArchiveMemberPrivateChannelHandler> logger, string clubMemberNickname);

    [LoggerMessage(LogLevel.Information, "Private text channel {TextChannelId} archived for club member '{clubMemberNickname}'.")]
    static partial void LogPrivateChannelArchived(ILogger<ArchiveMemberPrivateChannelHandler> logger, string clubMemberNickname, ulong textChannelId);
}

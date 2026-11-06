using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using Utilities;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// Moves a live private channel into a category of the member's current club, e.g. after a switch
/// to the other club. Returns whether the channel had to move.
/// </summary>
public sealed record MoveMemberPrivateChannelToClubCommand(ClubMember? ClubMember) : ICommand<Result<bool>>;

public sealed partial class MoveMemberPrivateChannelToClubHandler(
    MemberPrivateChannelManager manager,
    ILogger<MoveMemberPrivateChannelToClubHandler> logger)
    : IRequestHandler<MoveMemberPrivateChannelToClubCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(MoveMemberPrivateChannelToClubCommand request,
        CancellationToken cancellationToken)
    {
        var clubMember = request.ClubMember;

        if (clubMember?.PrivateTextChannelId is null)
        {
            return Error.NotFound(
                "member_private_channel.not_found",
                "No private text channel is configured for the given club member.");
        }

        var result = await PrivateChannelPlacementLock.RunAsync(async () =>
        {
            var placement = await manager.LoadAsync(cancellationToken).ConfigureAwait(false);
            var moved = await manager.MoveToClubAsync(placement, clubMember, cancellationToken).ConfigureAwait(false);

            if (moved is { IsSuccess: true, Value: true })
            {
                // The old club's overflow category may be empty now.
                await manager.PruneEmptyCategoriesAsync(placement, cancellationToken).ConfigureAwait(false);
            }

            return moved;
        }, cancellationToken).ConfigureAwait(false);

        if (result is { IsSuccess: true, Value: true })
        {
            LogMoved(logger, clubMember.User.Nickname, clubMember.PrivateTextChannelId.Value);
        }

        return result;
    }

    [LoggerMessage(LogLevel.Information,
        "Private text channel {TextChannelId} of '{clubMemberNickname}' moved to their club's category.")]
    static partial void LogMoved(ILogger<MoveMemberPrivateChannelToClubHandler> logger, string clubMemberNickname,
        ulong textChannelId);
}

using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using Utilities;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// Brings an archived private channel back: into a category of the member's club, renamed to the
/// member's current nickname, permissions re-applied, retention clock cleared. Used when someone
/// rejoins a club or re-links.
/// </summary>
public sealed record RestoreMemberPrivateChannelCommand(ClubMember? ClubMember) : ICommand<Result>;

public sealed partial class RestoreMemberPrivateChannelHandler(
    MemberPrivateChannelManager manager,
    ILogger<RestoreMemberPrivateChannelHandler> logger)
    : IRequestHandler<RestoreMemberPrivateChannelCommand, Result>
{
    public async Task<Result> Handle(RestoreMemberPrivateChannelCommand request, CancellationToken cancellationToken)
    {
        var clubMember = request.ClubMember;

        if (clubMember?.PrivateTextChannelId is null)
        {
            return Error.NotFound(
                "member_private_channel.not_found",
                "No private text channel is configured for the given club member.");
        }

        LogRestoringPrivateChannel(logger, clubMember.User.Nickname);

        var result = await PrivateChannelPlacementLock.RunAsync(async () =>
        {
            var placement = await manager.LoadAsync(cancellationToken).ConfigureAwait(false);
            return await manager.RestoreAsync(placement, clubMember, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            LogPrivateChannelRestored(logger, clubMember.User.Nickname, clubMember.PrivateTextChannelId.Value);
        }

        return result;
    }

    [LoggerMessage(LogLevel.Information, "Restoring archived private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogRestoringPrivateChannel(ILogger<RestoreMemberPrivateChannelHandler> logger, string clubMemberNickname);

    [LoggerMessage(LogLevel.Information, "Private text channel {TextChannelId} restored for club member '{clubMemberNickname}'.")]
    static partial void LogPrivateChannelRestored(ILogger<RestoreMemberPrivateChannelHandler> logger, string clubMemberNickname, ulong textChannelId);
}

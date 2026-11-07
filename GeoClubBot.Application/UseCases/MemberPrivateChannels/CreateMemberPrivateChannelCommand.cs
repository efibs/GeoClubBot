using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;

namespace UseCases.UseCases.MemberPrivateChannels;

public sealed record CreateMemberPrivateChannelCommand(ClubMember ClubMember) : ICommand<ulong?>;

public sealed partial class CreateMemberPrivateChannelHandler(
    MemberPrivateChannelManager manager,
    ILogger<CreateMemberPrivateChannelHandler> logger)
    : IRequestHandler<CreateMemberPrivateChannelCommand, ulong?>
{
    public async Task<ulong?> Handle(CreateMemberPrivateChannelCommand request, CancellationToken cancellationToken)
    {
        var clubMember = request.ClubMember;
        LogCreatingPrivateChannel(logger, clubMember.User.Nickname);

        var result = await PrivateChannelPlacementLock.RunAsync(async () =>
        {
            var placement = await manager.LoadAsync(cancellationToken).ConfigureAwait(false);
            return await manager.CreateAsync(placement, clubMember, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            LogPrivateTextChannelCouldNotBeCreatedForClubMember(logger, clubMember.User.Nickname, result.Error.Message);
            return null;
        }

        LogPrivateChannelCreated(logger, clubMember.User.Nickname, result.Value);
        return result.Value;
    }

    [LoggerMessage(LogLevel.Warning, "Private text channel could not be created for club member '{clubMemberNickname}': {Error}")]
    static partial void LogPrivateTextChannelCouldNotBeCreatedForClubMember(ILogger<CreateMemberPrivateChannelHandler> logger,
        string clubMemberNickname, string error);

    [LoggerMessage(LogLevel.Information, "Creating private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogCreatingPrivateChannel(ILogger<CreateMemberPrivateChannelHandler> logger, string clubMemberNickname);

    [LoggerMessage(LogLevel.Information, "Private text channel {TextChannelId} created for club member '{clubMemberNickname}'.")]
    static partial void LogPrivateChannelCreated(ILogger<CreateMemberPrivateChannelHandler> logger, string clubMemberNickname, ulong textChannelId);
}

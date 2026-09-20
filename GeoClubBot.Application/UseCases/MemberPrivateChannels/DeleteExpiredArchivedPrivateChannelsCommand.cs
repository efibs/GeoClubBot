using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// Deletes the private channels that have sat in the archive category longer than the configured
/// retention period, for members who are still in no club. Returns how many were deleted.
/// </summary>
public sealed record DeleteExpiredArchivedPrivateChannelsCommand : ICommand<Result<int>>;

public sealed partial class DeleteExpiredArchivedPrivateChannelsHandler(
    ISender mediator,
    IClubMemberRepository clubMembers,
    IOptions<MemberPrivateChannelsConfiguration> memberPrivateChannelsOptions,
    ILogger<DeleteExpiredArchivedPrivateChannelsHandler> logger)
    : IRequestHandler<DeleteExpiredArchivedPrivateChannelsCommand, Result<int>>
{
    public async Task<Result<int>> Handle(DeleteExpiredArchivedPrivateChannelsCommand request,
        CancellationToken cancellationToken)
    {
        var threshold = DateTimeOffset.UtcNow.Subtract(memberPrivateChannelsOptions.Value.ArchiveKeepTimeSpan);

        var expired = await clubMembers
            .ReadMembersWithExpiredArchivedPrivateChannelsAsync(threshold, cancellationToken)
            .ConfigureAwait(false);

        var deleted = 0;

        foreach (var clubMember in expired)
        {
            LogDeletingExpiredArchive(logger, clubMember.User.Nickname);

            var result = await mediator
                .Send(new DeleteMemberPrivateChannelCommand(clubMember), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                deleted++;
            }
            else
            {
                LogFailedToDeleteExpiredArchive(logger, clubMember.User.Nickname, result.Error.Message);
            }
        }

        return deleted;
    }

    [LoggerMessage(LogLevel.Information,
        "Archived private text channel of member '{clubMemberNickname}' has expired. Deleting it...")]
    static partial void LogDeletingExpiredArchive(ILogger<DeleteExpiredArchivedPrivateChannelsHandler> logger,
        string clubMemberNickname);

    [LoggerMessage(LogLevel.Warning,
        "Failed to delete the expired archived private channel of member '{clubMemberNickname}': {Error}")]
    static partial void LogFailedToDeleteExpiredArchive(ILogger<DeleteExpiredArchivedPrivateChannelsHandler> logger,
        string clubMemberNickname, string error);
}

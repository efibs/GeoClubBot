using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// Moves a member's private channel into the archive category instead of deleting it, so admins can
/// still talk to someone who was just kicked and a hop to the second club keeps its history.
/// </summary>
public sealed record ArchiveMemberPrivateChannelCommand(ClubMember? ClubMember) : ICommand<Result>;

public sealed partial class ArchiveMemberPrivateChannelHandler(
    IDiscordTextChannelAccess discordTextChannelAccess,
    IDiscordMessageAccess discordMessageAccess,
    IClubMemberRepository clubMembers,
    IOptions<MemberPrivateChannelsConfiguration> memberPrivateChannelsOptions,
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

        if (clubMember.PrivateTextChannelArchivedAt is not null)
        {
            // Already archived; leaving the timestamp alone keeps the retention clock running from
            // the first leave rather than restarting it.
            return Result.Success();
        }

        LogArchivingPrivateChannel(logger, clubMember.User.Nickname);

        var archivedAt = DateTimeOffset.UtcNow;
        var channel = new TextChannel(clubMember.PrivateTextChannelId.Value)
        {
            CategoryId = memberPrivateChannelsOptions.Value.ArchiveCategoryId
        };

        var moved = await discordTextChannelAccess
            .UpdateTextChannelAsync(channel, cancellationToken)
            .ConfigureAwait(false);

        if (!moved)
        {
            return Error.Unexpected(
                "member_private_channel.archive_failed",
                "The Discord channel could not be moved to the archive category.");
        }

        var trackedMember = await clubMembers
            .ReadForUpdateByUserIdAsync(clubMember.UserId, cancellationToken)
            .ConfigureAwait(false);
        trackedMember?.ArchivePrivateTextChannel(archivedAt);

        await SendArchiveNoticeAsync(clubMember, archivedAt, cancellationToken).ConfigureAwait(false);

        LogPrivateChannelArchived(logger, clubMember.User.Nickname, clubMember.PrivateTextChannelId.Value);
        return Result.Success();
    }

    private async Task SendArchiveNoticeAsync(ClubMember clubMember, DateTimeOffset archivedAt,
        CancellationToken cancellationToken)
    {
        var deletionDate = archivedAt.Add(memberPrivateChannelsOptions.Value.ArchiveKeepTimeSpan);

        // Deliberately no mention: nobody wants a ping for having been kicked.
        var messageBody = $"{clubMember.User.Nickname} is no longer in one of our clubs, so this channel " +
                          "has been archived. It stays readable for both sides, so we can still talk here. " +
                          $"If nobody rejoins a club it will be deleted on {deletionDate:yyyy-MM-dd}.";

        await discordMessageAccess
            .SendMessageAsync(messageBody, clubMember.PrivateTextChannelId!.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(LogLevel.Information, "Archiving private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogArchivingPrivateChannel(ILogger<ArchiveMemberPrivateChannelHandler> logger, string clubMemberNickname);

    [LoggerMessage(LogLevel.Information, "Private text channel {TextChannelId} archived for club member '{clubMemberNickname}'.")]
    static partial void LogPrivateChannelArchived(ILogger<ArchiveMemberPrivateChannelHandler> logger, string clubMemberNickname, ulong textChannelId);
}

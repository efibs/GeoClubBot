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
/// Brings an archived private channel back: out of the archive category, renamed to the member's
/// current nickname, retention clock cleared. Used when someone rejoins a club or re-links.
/// </summary>
public sealed record RestoreMemberPrivateChannelCommand(ClubMember? ClubMember) : ICommand<Result>;

public sealed partial class RestoreMemberPrivateChannelHandler(
    IDiscordTextChannelAccess discordTextChannelAccess,
    IDiscordMessageAccess discordMessageAccess,
    IClubMemberRepository clubMembers,
    IOptions<MemberPrivateChannelsConfiguration> memberPrivateChannelsOptions,
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

        // Nickname changes are skipped while the member is in no club, so the name can be stale by
        // the time they come back. Renaming here is the cheapest place to catch up.
        var channel = new TextChannel(clubMember.PrivateTextChannelId.Value)
        {
            Name = MemberPrivateChannelName.For(clubMember.User.Nickname),
            CategoryId = memberPrivateChannelsOptions.Value.CategoryId
        };

        var moved = await discordTextChannelAccess
            .UpdateTextChannelAsync(channel, cancellationToken)
            .ConfigureAwait(false);

        if (!moved)
        {
            return Error.Unexpected(
                "member_private_channel.restore_failed",
                "The Discord channel could not be moved out of the archive category.");
        }

        var trackedMember = await clubMembers
            .ReadForUpdateByUserIdAsync(clubMember.UserId, cancellationToken)
            .ConfigureAwait(false);
        trackedMember?.RestorePrivateTextChannel();

        if (clubMember.User.DiscordUserId is not null)
        {
            await discordMessageAccess
                .SendMessageAsync(
                    $"Welcome back <@{clubMember.User.DiscordUserId.Value}>! Your private channel is active again.",
                    clubMember.PrivateTextChannelId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        LogPrivateChannelRestored(logger, clubMember.User.Nickname, clubMember.PrivateTextChannelId.Value);
        return Result.Success();
    }

    [LoggerMessage(LogLevel.Information, "Restoring archived private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogRestoringPrivateChannel(ILogger<RestoreMemberPrivateChannelHandler> logger, string clubMemberNickname);

    [LoggerMessage(LogLevel.Information, "Private text channel {TextChannelId} restored for club member '{clubMemberNickname}'.")]
    static partial void LogPrivateChannelRestored(ILogger<RestoreMemberPrivateChannelHandler> logger, string clubMemberNickname, ulong textChannelId);
}

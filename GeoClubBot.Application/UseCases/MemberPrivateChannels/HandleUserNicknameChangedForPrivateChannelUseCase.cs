using Entities;
using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleUserNicknameChangedForPrivateChannelUseCase(
    IClubMemberRepository clubMembers,
    IDiscordTextChannelAccess discordTextChannelAccess,
    ILogger<HandleUserNicknameChangedForPrivateChannelUseCase> logger)
    : INotificationHandler<UserUpdatedEvent>
{
    public async Task Handle(UserUpdatedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            if (notification.OldDiscordUserId != notification.NewDiscordUserId)
            {
                return;
            }

            if (notification.OldNickname == notification.NewNickname)
            {
                return;
            }

            var clubMember = await clubMembers
                .ReadClubMemberByUserIdAsync(notification.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (clubMember?.ClubId is null)
            {
                return;
            }

            if (clubMember.PrivateTextChannelId.HasValue == false)
            {
                return;
            }

            var textChannelName = $"{clubMember.User.Nickname.ToLowerInvariant()}-private-channel";

            var newTextChannel = new TextChannel(clubMember.PrivateTextChannelId.Value)
            {
                Name = textChannelName
            };

            LogRenamingPrivateChannel(logger, clubMember.User.Nickname);

            await discordTextChannelAccess.UpdateTextChannelAsync(newTextChannel, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LogUnhandled(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Handling user updated for renaming private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogRenamingPrivateChannel(ILogger<HandleUserNicknameChangedForPrivateChannelUseCase> logger,
        string clubMemberNickname);

    [LoggerMessage(LogLevel.Error, "Error while handling HandleUserNicknameChangedForPrivateChannelUseCase")]
    static partial void LogUnhandled(ILogger<HandleUserNicknameChangedForPrivateChannelUseCase> logger, Exception ex);
}

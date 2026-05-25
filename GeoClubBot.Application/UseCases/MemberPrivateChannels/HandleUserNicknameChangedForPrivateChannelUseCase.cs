using Entities;
using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandleUserNicknameChangedForPrivateChannelUseCase(
    IUnitOfWork unitOfWork,
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

            var clubMember = await unitOfWork.ClubMembers
                .ReadClubMemberByUserIdAsync(notification.UserId)
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

            await discordTextChannelAccess.UpdateTextChannelAsync(newTextChannel).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandleUserNicknameChangedForPrivateChannelUseCase");
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Handling user updated for renaming private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogRenamingPrivateChannel(ILogger<HandleUserNicknameChangedForPrivateChannelUseCase> logger,
        string clubMemberNickname);
}

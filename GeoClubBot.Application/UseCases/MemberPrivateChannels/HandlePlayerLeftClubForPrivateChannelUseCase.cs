using Entities.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandlePlayerLeftClubForPrivateChannelUseCase(
    IDeleteMemberPrivateChannelUseCase useCase,
    IUnitOfWork unitOfWork,
    ILogger<HandlePlayerLeftClubForPrivateChannelUseCase> logger) : INotificationHandler<PlayerLeftClubEvent>
{
    public async Task Handle(PlayerLeftClubEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            LogLeaveDetected(logger, notification.Nickname);

            if (notification.DiscordUserId is null)
            {
                return;
            }

            var clubMember = await unitOfWork.ClubMembers
                .ReadClubMemberByUserIdAsync(notification.UserId)
                .ConfigureAwait(false);

            if (clubMember is null)
            {
                return;
            }

            await useCase.DeletePrivateChannelAsync(clubMember).ConfigureAwait(false);

            await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandlePlayerLeftClubForPrivateChannelUseCase");
        }
    }

    [LoggerMessage(LogLevel.Information,
        "Detected leave of member '{clubMemberNickname}'. Removing private channel...")]
    static partial void LogLeaveDetected(ILogger<HandlePlayerLeftClubForPrivateChannelUseCase> logger,
        string clubMemberNickname);
}

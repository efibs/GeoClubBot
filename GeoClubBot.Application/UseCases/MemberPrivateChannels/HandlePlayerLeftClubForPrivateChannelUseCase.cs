using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts;
using UseCases.UseCases.ClubMembers;

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
            LogLeaveDetected(logger, notification.ClubMember.User.Nickname);

            // Check if the user has his GeoGuessr account linked
            if (notification.ClubMember.User.DiscordUserId == null)
            {
                // If the user has not linked his account, nothing to do
                return;
            }

            // Delete the private channel
            await useCase.DeletePrivateChannelAsync(notification.ClubMember).ConfigureAwait(false);

            // Save the changes
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
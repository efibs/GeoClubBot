using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.MemberPrivateChannels;
using UseCases.OutputPorts;
using UseCases.UseCases.ClubMembers;

namespace UseCases.UseCases.MemberPrivateChannels;

public partial class HandlePlayerJoinedClubForPrivateChannelUseCase(
    ICreateMemberPrivateChannelUseCase useCase,
    IUnitOfWork unitOfWork,
    ILogger<HandlePlayerJoinedClubForPrivateChannelUseCase> logger) : INotificationHandler<PlayerJoinedClubEvent>
{
    public async Task Handle(PlayerJoinedClubEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            LogJoinDetected(logger, notification.ClubMember.User.Nickname);

            // Check if the user has his GeoGuessr account linked
            if (notification.ClubMember.User.DiscordUserId == null)
            {
                // If the user has not linked his account, nothing to do
                return;
            }

            // Check if the member already has a private channel
            if (notification.ClubMember.PrivateTextChannelId is not null)
            {
                return;
            }

            // Create the private channel
            await useCase.CreatePrivateChannelAsync(notification.ClubMember).ConfigureAwait(false);

            // Save the changes
            await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while handling HandlePlayerJoinedClubForPrivateChannelUseCase");
        }
    }

    [LoggerMessage(LogLevel.Information, "Detected join of member '{clubMemberNickname}'. Creating private channel...")]
    static partial void LogJoinDetected(ILogger<HandlePlayerJoinedClubForPrivateChannelUseCase> logger,
        string clubMemberNickname);
}
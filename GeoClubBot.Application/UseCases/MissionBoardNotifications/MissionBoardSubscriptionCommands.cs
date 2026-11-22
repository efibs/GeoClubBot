using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.MissionBoardNotifications;

/// <summary>
/// Subscribes the member to their club's mission board changes, or changes how long the
/// subscription lasts. Only a linked account that is in one of the clubs can subscribe; the club is
/// looked up again at every change, so a later switch is followed.
/// </summary>
/// <returns>The name of the club whose board they will hear about.</returns>
public sealed record SubscribeToMissionBoardCommand(ulong DiscordUserId, MissionBoardSubscriptionMode Mode) : ICommand<Result<string>>;

/// <summary>Ends the member's subscription; whether they had one.</summary>
public sealed record UnsubscribeFromMissionBoardCommand(ulong DiscordUserId) : ICommand<bool>;

/// <summary>The member's subscription mode; null when they have none.</summary>
public sealed record GetMissionBoardSubscriptionQuery(ulong DiscordUserId) : IQuery<MissionBoardSubscriptionMode?>;

public sealed class MissionBoardSubscriptionHandlers(
    IMissionBoardSubscriptionRepository subscriptions,
    IGeoGuessrUserRepository users,
    IClubMemberRepository clubMembers,
    IClubRepository clubs,
    IOptions<MissionBoardNotificationsConfiguration> config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    TimeProvider timeProvider)
    : IRequestHandler<SubscribeToMissionBoardCommand, Result<string>>,
      IRequestHandler<UnsubscribeFromMissionBoardCommand, bool>,
      IRequestHandler<GetMissionBoardSubscriptionQuery, MissionBoardSubscriptionMode?>
{
    public const string DisabledErrorCode = "mission_board_notifications.disabled";
    public const string NotLinkedErrorCode = "mission_board_notifications.not_linked";
    public const string NotInClubErrorCode = "mission_board_notifications.not_in_club";

    public async Task<Result<string>> Handle(SubscribeToMissionBoardCommand request, CancellationToken cancellationToken)
    {
        if (!config.Value.Enabled)
        {
            return Error.Validation(DisabledErrorCode, "Mission board alerts are switched off.");
        }

        var user = await users.ReadUserByDiscordUserIdAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound(NotLinkedErrorCode,
                "Board alerts follow your club's board, so I need to know your GeoGuessr account: link it first with `/gg-account link`.");
        }

        var member = await clubMembers.ReadClubMemberByUserIdAsync(user.UserId, cancellationToken).ConfigureAwait(false);
        if (member?.ClubId is not { } clubId || geoGuessrConfig.Value.Clubs.All(c => c.ClubId != clubId))
        {
            return Error.Validation(NotInClubErrorCode,
                "Your linked GeoGuessr account isn't in one of the clubs, so there is no board to alert you about.");
        }

        var now = timeProvider.GetUtcNow();
        var existing = await subscriptions.ReadForUpdateAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            subscriptions.Add(MissionBoardSubscription.Create(request.DiscordUserId, request.Mode, now));
        }
        else
        {
            existing.ChangeMode(request.Mode, now);
        }

        var club = await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false);
        return club?.Name ?? "your club";
    }

    public Task<bool> Handle(UnsubscribeFromMissionBoardCommand request, CancellationToken cancellationToken) =>
        subscriptions.DeleteAsync(request.DiscordUserId, cancellationToken);

    public async Task<MissionBoardSubscriptionMode?> Handle(GetMissionBoardSubscriptionQuery request, CancellationToken cancellationToken) =>
        (await subscriptions.ReadForUpdateAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false))?.Mode;
}

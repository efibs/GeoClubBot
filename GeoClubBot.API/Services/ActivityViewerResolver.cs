using System.Security.Claims;
using GeoClubBot.Authentication;
using MediatR;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace GeoClubBot.Services;

/// <summary>The authenticated viewer, resolved to their linked GeoGuessr identity and current club.</summary>
public sealed record ActivityViewer(string UserId, string Nickname, Guid? ClubId);

/// <summary>
/// Resolves the authenticated Discord viewer to their linked GeoGuessr identity and current club
/// membership, shared by the activity endpoints so they all agree on who is looking. Returns null
/// when there's no Discord identity or no linked account; a linked viewer who isn't in a club is
/// returned with a null <see cref="ActivityViewer.ClubId"/>.
/// </summary>
public class ActivityViewerResolver(ISender mediator, IClubMemberRepository clubMemberRepository)
{
    public async Task<ActivityViewer?> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.GetDiscordUserId() is not { } discordUserId)
        {
            return null;
        }

        var linked = await mediator
            .Send(new GetLinkedGeoGuessrUserQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);
        if (!linked.IsSuccess)
        {
            return null;
        }

        var member = await clubMemberRepository
            .ReadClubMemberByUserIdAsync(linked.Value.UserId, cancellationToken)
            .ConfigureAwait(false);

        return new ActivityViewer(linked.Value.UserId, linked.Value.Nickname, member?.ClubId);
    }
}

using Configuration;
using GeoClubBot.Authentication;
using GeoClubBot.DTOs;
using GeoClubBot.DTOs.Assemblers;
using GeoClubBot.Middleware;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.DailyChallenge;
using UseCases.UseCases.DailyMissionStatistics;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;

namespace GeoClubBot.Controllers;

/// <summary>
/// Backend for the Club Dashboard Discord Activity: the anonymous OAuth2 token exchange plus the
/// authenticated, aggregate dashboard payload (leaderboard + current challenge standings + mission
/// streaks, personalized to the viewing member).
/// </summary>
[ApiController]
[Route("/api/v1/activity")]
public class ActivityController(
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<ActivityCheckerConfiguration> activityConfig,
    IOptions<DiscordActivityConfiguration> discordActivityConfig) : ControllerBase
{
    /// <summary>The trailing window used to compute mission streaks (long enough to surface them).</summary>
    private const int StreakWindowDays = 90;

    /// <summary>Bounds the leaderboard depth so a caller can't degenerate or abuse the query.</summary>
    private const int MinHistoryDepth = 1;
    private const int MaxHistoryDepth = 60;

    /// <summary>
    /// The activity's public runtime configuration. The frontend fetches the Discord client id from
    /// here at boot instead of having it baked into the bundle, so the same image works for any
    /// Discord application (the operator only sets <c>DiscordActivity:ClientId</c>). Anonymous: the
    /// client id is public, and the frontend needs it before it can run the OAuth handshake.
    /// </summary>
    [HttpGet("config")]
    [AllowAnonymous]
    public ActionResult<ActivityConfigDto> GetConfig() =>
        Ok(new ActivityConfigDto(discordActivityConfig.Value.ClientId));

    [HttpPost("token")]
    [AllowAnonymous]
    public async Task<ActionResult<ActivityTokenResponse>> ExchangeToken(
        [FromBody] ActivityTokenRequest request,
        IDiscordOAuthService oauth,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return this.ToProblemDetails(Error.Validation("activity.code_required", "An authorization code is required."));
        }

        var result = await oauth.ExchangeCodeForTokenAsync(request.Code, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Ok(new ActivityTokenResponse(result.Value))
            : this.ToProblemDetails(result.Error);
    }

    [HttpGet("dashboard")]
    [Authorize(AuthenticationSchemes = DiscordActivityAuthenticationHandler.SchemeName)]
    public async Task<ActionResult<DashboardDto>> GetDashboard(
        [FromQuery] int? historyDepth,
        IClubRepository clubRepository,
        IClubMemberRepository clubMemberRepository,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        // The daily challenge is independent of clubs and open to everyone, so it's always included
        // regardless of who is viewing or whether they belong to a club.
        var challenges = await mediator
            .Send(new GetCurrentChallengeResultsQuery(), cancellationToken)
            .ConfigureAwait(false);

        // Resolve the viewer's linked GeoGuessr identity (used to highlight their rows wherever they
        // appear, e.g. the challenge standings) and the club they currently belong to. Only the
        // club-scoped panels — leaderboard and mission streaks — are tied to that club, and they stay
        // empty when the viewer isn't a member of any club.
        var viewer = await ResolveViewerAsync(mediator, clubMemberRepository, cancellationToken).ConfigureAwait(false);

        Entities.Club? club = null;
        IReadOnlyList<Entities.ClubMemberAverageXp> leaderboard = [];
        IReadOnlyList<MemberMissionStreak> streaks = [];

        if (viewer?.ClubId is { } clubId)
        {
            club = await clubRepository.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false);
            if (club is not null)
            {
                var depth = Math.Clamp(
                    historyDepth ?? GetDefaultHistoryDepth(clubId),
                    MinHistoryDepth,
                    MaxHistoryDepth);

                var leaderboardResult = await mediator
                    .Send(new GetActivityLeaderboardQuery(ClubName: club.Name, HistoryDepth: depth), cancellationToken)
                    .ConfigureAwait(false);
                leaderboard = leaderboardResult.Leaderboard ?? [];

                streaks = await mediator
                    .Send(new GetDailyMissionStreaksQuery(clubId, StreakWindowDays), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var dto = DashboardDtoAssembler.Assemble(club, viewer?.Nickname, leaderboard, challenges, streaks);

        return Ok(dto);
    }

    /// <summary>
    /// Resolves the authenticated Discord viewer to their linked GeoGuessr identity and current club
    /// membership. Returns null when there's no Discord identity or no linked account (nobody to
    /// highlight); a linked viewer who isn't in a club is returned with a null <see cref="ViewerContext.ClubId"/>.
    /// </summary>
    private async Task<ViewerContext?> ResolveViewerAsync(
        ISender mediator,
        IClubMemberRepository clubMemberRepository,
        CancellationToken cancellationToken)
    {
        if (User.GetDiscordUserId() is not { } discordUserId)
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

        return new ViewerContext(member?.ClubId, linked.Value.Nickname);
    }

    private int GetDefaultHistoryDepth(Guid clubId)
    {
        var clubEntry = geoGuessrConfig.Value.Clubs.FirstOrDefault(c => c.ClubId == clubId)
                        ?? geoGuessrConfig.Value.MainClub;
        return clubEntry.GetAverageXpHistoryDepth(activityConfig.Value);
    }

    private sealed record ViewerContext(Guid? ClubId, string Nickname);
}

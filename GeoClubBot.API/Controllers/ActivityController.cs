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
    IOptions<ActivityCheckerConfiguration> activityConfig) : ControllerBase
{
    /// <summary>The trailing window used to compute mission streaks (long enough to surface them).</summary>
    private const int StreakWindowDays = 90;

    /// <summary>Bounds the leaderboard depth so a caller can't degenerate or abuse the query.</summary>
    private const int MinHistoryDepth = 1;
    private const int MaxHistoryDepth = 60;

    private Guid MainClubId => geoGuessrConfig.Value.MainClub.ClubId;

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
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var club = await clubRepository.ReadClubByIdAsync(MainClubId, cancellationToken).ConfigureAwait(false);
        if (club is null)
        {
            return this.ToProblemDetails(Error.NotFound("club.not_found", $"Club with id {MainClubId} was not found."));
        }

        var depth = Math.Clamp(
            historyDepth ?? geoGuessrConfig.Value.MainClub.GetAverageXpHistoryDepth(activityConfig.Value),
            MinHistoryDepth,
            MaxHistoryDepth);

        var leaderboard = await mediator
            .Send(new GetActivityLeaderboardQuery(ClubName: null, HistoryDepth: depth), cancellationToken)
            .ConfigureAwait(false);

        var challenges = await mediator
            .Send(new GetCurrentChallengeResultsQuery(), cancellationToken)
            .ConfigureAwait(false);

        var streaks = await mediator
            .Send(new GetDailyMissionStreaksQuery(MainClubId, StreakWindowDays), cancellationToken)
            .ConfigureAwait(false);

        var viewerNickname = await ResolveViewerNicknameAsync(mediator, cancellationToken).ConfigureAwait(false);

        var dto = DashboardDtoAssembler.Assemble(
            club,
            viewerNickname,
            leaderboard.Leaderboard ?? [],
            challenges,
            streaks);

        return Ok(dto);
    }

    private async Task<string?> ResolveViewerNicknameAsync(ISender mediator, CancellationToken cancellationToken)
    {
        if (User.GetDiscordUserId() is not { } discordUserId)
        {
            return null;
        }

        var viewer = await mediator
            .Send(new GetLinkedGeoGuessrUserQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);

        return viewer.IsSuccess ? viewer.Value.Nickname : null;
    }
}

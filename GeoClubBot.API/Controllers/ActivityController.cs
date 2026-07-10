using System.Globalization;
using Configuration;
using GeoClubBot.Authentication;
using GeoClubBot.DTOs;
using GeoClubBot.DTOs.Assemblers;
using GeoClubBot.Middleware;
using GeoClubBot.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.DailyChallenge;
using UseCases.UseCases.DailyMissionReminder;
using UseCases.UseCases.DailyMissionStatistics;
using UseCases.UseCases.GeoGuessrAccountLinking;
using UseCases.UseCases.RankedSystem;
using Utilities;

namespace GeoClubBot.Controllers;

/// <summary>
/// Backend for the Club Dashboard Discord Activity: the anonymous OAuth2 token exchange plus the
/// authenticated, aggregate dashboard payload (leaderboard + current challenge standings + mission
/// streaks, personalized to the viewing member).
/// </summary>
[ApiController]
[Route("/api/v1/activity")]
// Secure by default: every action requires a valid Discord Activity token unless it explicitly
// opts out with [AllowAnonymous] (the two public endpoints below). [AllowAnonymous] overrides this
// class-level policy at runtime, so the OAuth handshake endpoints stay open while any future action
// is authenticated unless deliberately exempted.
[Authorize(AuthenticationSchemes = DiscordActivityAuthenticationHandler.SchemeName)]
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

    /// <summary>Bounds for the self-activity window (defaults to the last week).</summary>
    private const int DefaultActivityDaysBack = 7;
    private const int MaxActivityDaysBack = 60;

    /// <summary>Bounds for the mission-statistics window (mirrors the query handler's own clamp).</summary>
    private const int DefaultMissionStatsDaysBack = 30;
    private const int MaxMissionStatsDaysBack = 365;

    private static readonly Error NotLinkedError = Error.NotFound(
        "activity.not_linked",
        "No GeoGuessr account is linked to your Discord account.");

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
    [EnableRateLimiting(RateLimitPolicies.ActivityTokenExchange)]
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

    // Authorization is inherited from the controller-level [Authorize] above.
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> GetDashboard(
        [FromQuery] int? historyDepth,
        IClubRepository clubRepository,
        ActivityViewerResolver viewerResolver,
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
        var viewer = await viewerResolver.ResolveAsync(User, cancellationToken).ConfigureAwait(false);

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
    /// The viewer's own session context: identity, linked GeoGuessr account, club, any open linking
    /// request (with its one-time password — the caller owns it), and whether they pass the admin
    /// policy. Fetched once by the frontend after the handshake to decide which tabs to offer; the
    /// admin flag is purely cosmetic there because every admin endpoint re-evaluates the same policy.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> GetMe(
        ActivityViewerResolver viewerResolver,
        IClubRepository clubRepository,
        IAuthorizationService authorizationService,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        if (User.GetDiscordUserId() is not { } discordUserId)
        {
            return Unauthorized();
        }

        // The exact policy the admin endpoints enforce — evaluated here so the frontend's admin
        // flag can never diverge from what the server would actually allow.
        var isAdmin = (await authorizationService
            .AuthorizeAsync(User, ActivityAdminRequirement.PolicyName)
            .ConfigureAwait(false)).Succeeded;

        var viewer = await viewerResolver.ResolveAsync(User, cancellationToken).ConfigureAwait(false);

        Entities.Club? club = null;
        if (viewer?.ClubId is { } clubId)
        {
            club = await clubRepository.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false);
        }

        // Only the caller's own open request is ever looked up, so exposing its OTP is safe here.
        var openRequest = await mediator
            .Send(new GetOpenAccountLinkingRequestQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);

        var dto = new MeDto(
            discordUserId.ToString(),
            isAdmin,
            viewer is null ? null : new LinkedAccountDto(viewer.UserId, viewer.Nickname),
            club is null ? null : ClubDtoAssembler.AssembleDto(club),
            openRequest.IsSuccess
                ? new LinkRequestDto(openRequest.Value.GeoGuessrUserId, openRequest.Value.OneTimePassword)
                : null);

        return Ok(dto);
    }

    /// <summary>The viewer's own XP + daily-mission activity over the last <paramref name="daysBack"/> days.</summary>
    [HttpGet("me/activity")]
    public async Task<ActionResult<WeekActivityDto>> GetMyActivity(
        [FromQuery] int? daysBack,
        ActivityViewerResolver viewerResolver,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var viewer = await viewerResolver.ResolveAsync(User, cancellationToken).ConfigureAwait(false);
        if (viewer is null)
        {
            return this.ToProblemDetails(NotLinkedError);
        }

        var days = Math.Clamp(daysBack ?? DefaultActivityDaysBack, 1, MaxActivityDaysBack);
        var activity = await mediator
            .Send(new GetActivityLastDaysQuery(viewer.UserId, days), cancellationToken)
            .ConfigureAwait(false);

        return Ok(ActivityMemberDtoAssembler.AssembleWeekActivity(activity));
    }

    /// <summary>The viewer's GeoGuessr profile plus ranked stats (fetched live from GeoGuessr).</summary>
    [HttpGet("me/profile")]
    public async Task<ActionResult<ProfileDto>> GetMyProfile(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        if (User.GetDiscordUserId() is not { } discordUserId)
        {
            return Unauthorized();
        }

        var profile = await mediator
            .Send(new GetGeoGuessrProfileQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);
        if (!profile.IsSuccess)
        {
            return this.ToProblemDetails(profile.Error);
        }

        // Ranked stats are a nice-to-have: they 404 for players who never touched ranked, so a
        // failure degrades to "no ranked section" instead of failing the whole profile.
        var rankedProgress = await mediator
            .Send(new GetUserRankedProgressQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);
        var rankedPeak = await mediator
            .Send(new GetUserRankedPeakRatingQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);

        var dto = ActivityMemberDtoAssembler.AssembleProfile(
            profile.Value,
            rankedProgress.IsSuccess ? rankedProgress.Value : null,
            rankedPeak.IsSuccess ? rankedPeak.Value : null);

        return Ok(dto);
    }

    /// <summary>
    /// Aggregated daily-mission statistics, scoped to the viewer's club when they belong to one and
    /// across all clubs otherwise (the same aggregate the public slash command shows).
    /// </summary>
    [HttpGet("missions/stats")]
    public async Task<ActionResult<MissionStatsDto>> GetMissionStats(
        [FromQuery] int? daysBack,
        ActivityViewerResolver viewerResolver,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var viewer = await viewerResolver.ResolveAsync(User, cancellationToken).ConfigureAwait(false);
        var days = Math.Clamp(daysBack ?? DefaultMissionStatsDaysBack, 1, MaxMissionStatsDaysBack);

        var statistics = await mediator
            .Send(new GetDailyMissionStatisticsQuery(viewer?.ClubId, days), cancellationToken)
            .ConfigureAwait(false);

        return statistics.IsSuccess
            ? Ok(ActivityMemberDtoAssembler.AssembleMissionStats(statistics.Value))
            : this.ToProblemDetails(statistics.Error);
    }

    /// <summary>Today's XP of the viewer's club (falls back to the configured default club).</summary>
    [HttpGet("club/todays-xp")]
    public async Task<ActionResult<TodaysXpDto>> GetClubTodaysXp(
        [FromQuery] bool includeWeeklies,
        ActivityViewerResolver viewerResolver,
        IClubRepository clubRepository,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        string? clubName = null;
        var viewer = await viewerResolver.ResolveAsync(User, cancellationToken).ConfigureAwait(false);
        if (viewer?.ClubId is { } clubId)
        {
            clubName = (await clubRepository.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false))?.Name;
        }

        var result = await mediator
            .Send(new GetClubTodaysXpQuery(clubName, includeWeeklies), cancellationToken)
            .ConfigureAwait(false);

        return Ok(new TodaysXpDto(result.Xp, result.ClubName));
    }

    /// <summary>The viewer's daily-mission reminders, ordered by time (empty when none are configured).</summary>
    [HttpGet("me/reminders")]
    public async Task<ActionResult<IReadOnlyList<ReminderDto>>> GetMyReminders(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        if (User.GetDiscordUserId() is not { } discordUserId)
        {
            return Unauthorized();
        }

        var reminders = await mediator
            .Send(new ListDailyMissionRemindersQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);

        return Ok(reminders.Select(ActivityMemberDtoAssembler.AssembleReminder).ToList());
    }

    /// <summary>
    /// Adds a daily-mission reminder for the viewer (or updates the one at the same time). The reminder
    /// is persisted even when the confirmation DM can't be delivered — the response reports the DM
    /// outcome separately so the frontend can tell the viewer to enable DMs. Returns 409 when the viewer
    /// already has the maximum number of reminders.
    /// </summary>
    [HttpPost("me/reminders")]
    public async Task<ActionResult<AddReminderResultDto>> AddMyReminder(
        [FromBody] AddReminderRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        if (User.GetDiscordUserId() is not { } discordUserId)
        {
            return Unauthorized();
        }

        if (!TimeOnly.TryParseExact(request.LocalTime, "HH\\:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var localTime))
        {
            return this.ToProblemDetails(Error.Validation(
                "activity.invalid_reminder_time",
                "The reminder time must be in HH:mm format."));
        }

        var timeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? null : request.TimeZoneId.Trim();
        var customMessage = string.IsNullOrWhiteSpace(request.CustomMessage) ? null : request.CustomMessage.Trim();

        // The command validates the time zone + message length (FluentValidation), enforces the per-user
        // limit (Conflict), and reports the confirmation-DM delivery inside its result value; the reminder
        // itself is persisted regardless of DM delivery.
        var result = await mediator
            .Send(new AddDailyMissionReminderCommand(discordUserId, localTime, timeZoneId, customMessage), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return this.ToProblemDetails(result.Error);
        }

        var reminders = await mediator
            .Send(new ListDailyMissionRemindersQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);
        var added = reminders.FirstOrDefault(r => r.Id == result.Value.ReminderId);
        if (added is null)
        {
            return this.ToProblemDetails(Error.Unexpected(
                "activity.reminder_not_persisted",
                "The reminder could not be saved."));
        }

        var dmResult = result.Value.DmDelivery;
        return Ok(new AddReminderResultDto(
            ActivityMemberDtoAssembler.AssembleReminder(added),
            dmResult.IsSuccess,
            dmResult.IsSuccess ? null : dmResult.Error.Code));
    }

    /// <summary>Removes one of the viewer's daily-mission reminders.</summary>
    [HttpDelete("me/reminders/{id:guid}")]
    public async Task<IActionResult> RemoveMyReminder(
        Guid id,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        if (User.GetDiscordUserId() is not { } discordUserId)
        {
            return Unauthorized();
        }

        var result = await mediator
            .Send(new RemoveDailyMissionReminderCommand(discordUserId, id), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? NoContent() : this.ToProblemDetails(result.Error);
    }

    /// <summary>
    /// Starts linking the viewer's Discord account to a GeoGuessr account, mirroring the
    /// <c>/gg-account link</c> slash-command flow: the response carries the one-time password the
    /// viewer must send to an admin via GeoGuessr DM. Admins get a heads-up in their channel and
    /// see the open request in the dashboard's admin area.
    /// </summary>
    [HttpPost("me/link-request")]
    public async Task<ActionResult<LinkRequestDto>> StartMyLinkRequest(
        [FromBody] StartLinkRequest request,
        ISender mediator,
        IDiscordMessageAccess discordMessageAccess,
        IOptions<GeoGuessrAccountLinkingConfiguration> linkingConfig,
        ILogger<ActivityController> logger,
        CancellationToken cancellationToken)
    {
        if (User.GetDiscordUserId() is not { } discordUserId)
        {
            return Unauthorized();
        }

        var geoGuessrUserId = request.GeoGuessrUserId.Trim();

        // Pre-checks mirroring GeoGuessrAccountLinkPublicModule: neither side may already be
        // linked, and only one open request per user.
        var linkedDiscordUser = await mediator
            .Send(new GetLinkedDiscordUserIdQuery(geoGuessrUserId), cancellationToken)
            .ConfigureAwait(false);
        if (linkedDiscordUser.IsSuccess)
        {
            return this.ToProblemDetails(Error.Conflict(
                "account_linking.geoguessr_already_linked",
                "This GeoGuessr account is already linked to a Discord account. Contact an admin if you think this is a mistake."));
        }

        var linkedGeoGuessrUser = await mediator
            .Send(new GetLinkedGeoGuessrUserQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);
        if (linkedGeoGuessrUser.IsSuccess)
        {
            return this.ToProblemDetails(Error.Conflict(
                "account_linking.already_linked",
                "Your Discord account is already linked to a GeoGuessr account. Contact an admin if you think this is a mistake."));
        }

        var openRequest = await mediator
            .Send(new GetOpenAccountLinkingRequestQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);
        if (openRequest.IsSuccess)
        {
            return this.ToProblemDetails(Error.Conflict(
                "account_linking.request_pending",
                "You already have an open linking request. Cancel it first to start over."));
        }

        var oneTimePassword = await mediator
            .Send(new StartAccountLinkingCommand(discordUserId, geoGuessrUserId), cancellationToken)
            .ConfigureAwait(false);
        if (!oneTimePassword.IsSuccess)
        {
            return this.ToProblemDetails(oneTimePassword.Error);
        }

        // Heads-up for the admins (the dashboard flow has no slash-command message). Best effort:
        // the request is already persisted, so a Discord hiccup must not fail the response — the
        // admin area lists open requests either way.
        try
        {
            await discordMessageAccess.SendMessageAsync(
                    $"User <@{discordUserId}> (id: {discordUserId}) started the linking process for GeoGuessr " +
                    $"account {geoGuessrUserId} from the Club Dashboard. Complete or cancel it in the " +
                    "dashboard's admin area once they've sent you their password inside GeoGuessr.\n\n" +
                    "**Only accept the password if it was sent to you by the correct user inside GeoGuessr!**",
                    linkingConfig.Value.AdminChannelId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Failed to post the account-linking heads-up to the admin channel for user {DiscordUserId}.",
                discordUserId);
        }

        return Ok(new LinkRequestDto(geoGuessrUserId, oneTimePassword.Value));
    }

    /// <summary>Cancels the viewer's own open linking request.</summary>
    [HttpDelete("me/link-request")]
    public async Task<IActionResult> CancelMyLinkRequest(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        if (User.GetDiscordUserId() is not { } discordUserId)
        {
            return Unauthorized();
        }

        var openRequest = await mediator
            .Send(new GetOpenAccountLinkingRequestQuery(discordUserId), cancellationToken)
            .ConfigureAwait(false);
        if (!openRequest.IsSuccess)
        {
            return this.ToProblemDetails(openRequest.Error);
        }

        var result = await mediator
            .Send(new CancelAccountLinkingCommand(discordUserId, openRequest.Value.GeoGuessrUserId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? NoContent() : this.ToProblemDetails(result.Error);
    }

    private int GetDefaultHistoryDepth(Guid clubId)
    {
        var clubEntry = geoGuessrConfig.Value.Clubs.FirstOrDefault(c => c.ClubId == clubId)
                        ?? geoGuessrConfig.Value.MainClub;
        return clubEntry.GetAverageXpHistoryDepth(activityConfig.Value);
    }
}

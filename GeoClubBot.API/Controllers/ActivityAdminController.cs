using Configuration;
using GeoClubBot.Authentication;
using GeoClubBot.DTOs;
using GeoClubBot.DTOs.Assemblers;
using GeoClubBot.Middleware;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.ClubMembers;
using UseCases.UseCases.Excuses;
using UseCases.UseCases.GeoGuessrAccountLinking;
using UseCases.UseCases.Strikes;
using Utilities;

namespace GeoClubBot.Controllers;

/// <summary>
/// Admin-only backend for the Club Dashboard Activity: strikes, excuses, per-member activity and
/// account-linking administration. Every action requires the viewer to hold the Discord
/// Administrator permission in the configured guild — the same gate as the admin slash commands —
/// enforced server-side by the <see cref="ActivityAdminRequirement"/> policy. Nothing in this
/// controller may ever be <c>[AllowAnonymous]</c> or weaken the class-level policy: member-facing
/// data belongs in <see cref="ActivityController"/> instead.
/// </summary>
[ApiController]
[Route("/api/v1/activity/admin")]
[Authorize(
    AuthenticationSchemes = DiscordActivityAuthenticationHandler.SchemeName,
    Policy = ActivityAdminRequirement.PolicyName)]
public partial class ActivityAdminController(
    IOptions<ActivityCheckerConfiguration> activityConfig,
    ILogger<ActivityAdminController> logger) : ControllerBase
{
    /// <summary>Bounds for the per-member activity window (mirrors the member self-view).</summary>
    private const int DefaultActivityDaysBack = 7;
    private const int MaxActivityDaysBack = 60;

    private TimeSpan StrikeDecay => activityConfig.Value.StrikeDecayTimeSpan;

    /// <summary>The acting admin, for the audit log lines every write emits.</summary>
    private ulong AdminDiscordUserId => User.GetDiscordUserId() ?? 0;

    [HttpGet("last-check-time")]
    public async Task<ActionResult<LastCheckTimeDto>> GetLastCheckTime(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var lastCheckTime = await mediator
            .Send(new GetLastCheckTimeQuery(), cancellationToken)
            .ConfigureAwait(false);

        return Ok(new LastCheckTimeDto(lastCheckTime));
    }

    /// <summary>Every strike of every member, incl. revoked and expired ones.</summary>
    [HttpGet("strikes")]
    public async Task<ActionResult<IReadOnlyList<AdminStrikeDto>>> GetAllStrikes(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var strikes = await mediator
            .Send(new ReadAllStrikesQuery(), cancellationToken)
            .ConfigureAwait(false);

        return Ok(strikes
            .Select(strike => ActivityAdminDtoAssembler.AssembleStrike(strike, StrikeDecay))
            .ToList());
    }

    /// <summary>Members that currently carry at least one active strike.</summary>
    [HttpGet("strikes/relevant")]
    public async Task<ActionResult<IReadOnlyList<AdminRelevantStrikeDto>>> GetRelevantStrikes(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var relevantStrikes = await mediator
            .Send(new ReadAllRelevantStrikesQuery(), cancellationToken)
            .ConfigureAwait(false);

        return Ok(relevantStrikes
            .Select(entry => new AdminRelevantStrikeDto(entry.MemberNickname, entry.NumActiveStrikes))
            .ToList());
    }

    [HttpGet("members/{nickname}/strikes")]
    public async Task<ActionResult<AdminMemberStrikesDto>> GetMemberStrikes(
        string nickname,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var status = await mediator
            .Send(new ReadMemberStrikesQuery(nickname), cancellationToken)
            .ConfigureAwait(false);
        if (!status.IsSuccess)
        {
            return this.ToProblemDetails(status.Error);
        }

        var dto = new AdminMemberStrikesDto(
            status.Value.NumActiveStrikes,
            status.Value.Strikes
                .Select(strike => ActivityAdminDtoAssembler.AssembleStrike(strike, nickname, StrikeDecay))
                .ToList());

        return Ok(dto);
    }

    /// <summary>All excuses, optionally filtered to one member.</summary>
    [HttpGet("excuses")]
    public async Task<ActionResult<IReadOnlyList<AdminExcuseDto>>> GetExcuses(
        [FromQuery] string? nickname,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var excuses = await mediator
            .Send(new ReadExcusesQuery(nickname), cancellationToken)
            .ConfigureAwait(false);

        return Ok(excuses.Select(ActivityAdminDtoAssembler.AssembleExcuse).ToList());
    }

    /// <summary>A member's XP + daily-mission activity, looked up by nickname (admin view).</summary>
    [HttpGet("members/{nickname}/activity")]
    public async Task<ActionResult<WeekActivityDto>> GetMemberActivity(
        string nickname,
        [FromQuery] int? daysBack,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var member = await mediator
            .Send(new ReadOrSyncClubMemberByNicknameQuery(nickname), cancellationToken)
            .ConfigureAwait(false);
        if (!member.IsSuccess)
        {
            return this.ToProblemDetails(member.Error);
        }

        var days = Math.Clamp(daysBack ?? DefaultActivityDaysBack, 1, MaxActivityDaysBack);
        var activity = await mediator
            .Send(new GetActivityLastDaysQuery(member.Value.UserId, days), cancellationToken)
            .ConfigureAwait(false);

        return Ok(ActivityMemberDtoAssembler.AssembleWeekActivity(activity));
    }

    [HttpGet("members/{nickname}/statistics")]
    public async Task<ActionResult<PlayerStatisticsDto>> GetMemberStatistics(
        string nickname,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var statistics = await mediator
            .Send(new PlayerStatisticsQuery(nickname), cancellationToken)
            .ConfigureAwait(false);
        if (statistics is null)
        {
            return this.ToProblemDetails(Error.NotFound(
                "activity.player_statistics_not_found",
                "There is no recorded history for this member."));
        }

        return Ok(ActivityAdminDtoAssembler.AssemblePlayerStatistics(statistics));
    }

    [HttpGet("club/statistics")]
    public async Task<ActionResult<ClubStatisticsDto>> GetClubStatistics(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var statistics = await mediator
            .Send(new ClubStatisticsQuery(), cancellationToken)
            .ConfigureAwait(false);
        if (statistics is null)
        {
            return this.ToProblemDetails(Error.NotFound(
                "activity.club_statistics_not_found",
                "There is no recorded history for the club yet."));
        }

        return Ok(ActivityAdminDtoAssembler.AssembleClubStatistics(statistics));
    }

    /// <summary>Open account-linking requests — deliberately without their one-time passwords.</summary>
    [HttpGet("link-requests")]
    public async Task<ActionResult<IReadOnlyList<AdminLinkRequestDto>>> GetLinkRequests(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var requests = await mediator
            .Send(new ReadOpenAccountLinkingRequestsQuery(), cancellationToken)
            .ConfigureAwait(false);

        return Ok(requests.Select(ActivityAdminDtoAssembler.AssembleLinkRequest).ToList());
    }

    [HttpPost("strikes")]
    public async Task<ActionResult<StrikeCreatedDto>> AddStrike(
        [FromBody] AddStrikeRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator
            .Send(new AddStrikeCommand(request.MemberNickname, request.StrikeDate), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result.Error);
        }

        LogStrikeAdded(AdminDiscordUserId, request.MemberNickname, request.StrikeDate);
        return Created(
            $"/api/v1/activity/admin/members/{Uri.EscapeDataString(request.MemberNickname)}/strikes",
            new StrikeCreatedDto(result.Value));
    }

    [HttpPost("strikes/{strikeId:guid}/revoke")]
    public async Task<ActionResult<AdminStrikeDto>> RevokeStrike(
        Guid strikeId,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator
            .Send(new RevokeStrikeCommand(strikeId), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result.Error);
        }

        LogStrikeRevoked(AdminDiscordUserId, strikeId);
        return Ok(ActivityAdminDtoAssembler.AssembleStrike(result.Value, StrikeDecay));
    }

    [HttpPost("strikes/{strikeId:guid}/unrevoke")]
    public async Task<ActionResult<AdminStrikeDto>> UnrevokeStrike(
        Guid strikeId,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator
            .Send(new UnrevokeStrikeCommand(strikeId), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result.Error);
        }

        LogStrikeUnrevoked(AdminDiscordUserId, strikeId);
        return Ok(ActivityAdminDtoAssembler.AssembleStrike(result.Value, StrikeDecay));
    }

    [HttpPost("excuses")]
    public async Task<ActionResult<ExcuseCreatedDto>> AddExcuse(
        [FromBody] AddExcuseRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator
            .Send(new AddExcuseCommand(request.MemberNickname, request.From, request.To), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result.Error);
        }

        LogExcuseAdded(AdminDiscordUserId, request.MemberNickname, request.From, request.To);
        return Created("/api/v1/activity/admin/excuses", new ExcuseCreatedDto(result.Value));
    }

    [HttpPut("excuses/{excuseId:guid}")]
    public async Task<ActionResult<AdminExcuseDto>> UpdateExcuse(
        Guid excuseId,
        [FromBody] UpdateExcuseRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator
            .Send(new UpdateExcuseCommand(excuseId, request.From, request.To), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result.Error);
        }

        LogExcuseUpdated(AdminDiscordUserId, excuseId, request.From, request.To);
        return Ok(ActivityAdminDtoAssembler.AssembleExcuse(result.Value));
    }

    [HttpDelete("excuses/{excuseId:guid}")]
    public async Task<IActionResult> RemoveExcuse(
        Guid excuseId,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator
            .Send(new RemoveExcuseCommand(excuseId), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result.Error);
        }

        LogExcuseRemoved(AdminDiscordUserId, excuseId);
        return NoContent();
    }

    /// <summary>
    /// Completes a linking request. The admin passes the one-time password the member sent them via
    /// GeoGuessr DM — that message is what proves account ownership, exactly like the Discord flow.
    /// </summary>
    [HttpPost("link-requests/complete")]
    public async Task<ActionResult<LinkCompletedDto>> CompleteLinkRequest(
        [FromBody] CompleteLinkRequestRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(request.DiscordUserId, out var discordUserId))
        {
            return this.ToProblemDetails(InvalidDiscordUserIdError);
        }

        var result = await mediator
            .Send(new CompleteAccountLinkingCommand(discordUserId, request.GeoGuessrUserId, request.OneTimePassword),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result.Error);
        }

        LogLinkCompleted(AdminDiscordUserId, discordUserId, request.GeoGuessrUserId);
        return Ok(new LinkCompletedDto(result.Value.UserId, result.Value.Nickname));
    }

    [HttpPost("link-requests/cancel")]
    public async Task<IActionResult> CancelLinkRequest(
        [FromBody] CancelLinkRequestRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(request.DiscordUserId, out var discordUserId))
        {
            return this.ToProblemDetails(InvalidDiscordUserIdError);
        }

        var result = await mediator
            .Send(new CancelAccountLinkingCommand(discordUserId, request.GeoGuessrUserId), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result.Error);
        }

        LogLinkCancelled(AdminDiscordUserId, discordUserId, request.GeoGuessrUserId);
        return NoContent();
    }

    [HttpPost("links/unlink")]
    public async Task<IActionResult> UnlinkAccounts(
        [FromBody] UnlinkAccountsRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(request.DiscordUserId, out var discordUserId))
        {
            return this.ToProblemDetails(InvalidDiscordUserIdError);
        }

        var result = await mediator
            .Send(new UnlinkAccountsCommand(discordUserId, request.GeoGuessrUserId), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return this.ToProblemDetails(result.Error);
        }

        LogUnlinked(AdminDiscordUserId, discordUserId, request.GeoGuessrUserId);
        return NoContent();
    }

    private static readonly Error InvalidDiscordUserIdError = Error.Validation(
        "activity.invalid_discord_user_id",
        "The Discord user id must be a numeric snowflake.");

    // Audit trail: every admin write logs the acting admin and the target.

    [LoggerMessage(LogLevel.Information,
        "Dashboard admin {AdminDiscordUserId} added a strike to member {MemberNickname} dated {StrikeDate}.")]
    partial void LogStrikeAdded(ulong adminDiscordUserId, string memberNickname, DateTimeOffset strikeDate);

    [LoggerMessage(LogLevel.Information,
        "Dashboard admin {AdminDiscordUserId} revoked strike {StrikeId}.")]
    partial void LogStrikeRevoked(ulong adminDiscordUserId, Guid strikeId);

    [LoggerMessage(LogLevel.Information,
        "Dashboard admin {AdminDiscordUserId} unrevoked strike {StrikeId}.")]
    partial void LogStrikeUnrevoked(ulong adminDiscordUserId, Guid strikeId);

    [LoggerMessage(LogLevel.Information,
        "Dashboard admin {AdminDiscordUserId} added an excuse for member {MemberNickname} from {From} to {To}.")]
    partial void LogExcuseAdded(ulong adminDiscordUserId, string memberNickname, DateTimeOffset from, DateTimeOffset to);

    [LoggerMessage(LogLevel.Information,
        "Dashboard admin {AdminDiscordUserId} updated excuse {ExcuseId} to {From} – {To}.")]
    partial void LogExcuseUpdated(ulong adminDiscordUserId, Guid excuseId, DateTimeOffset from, DateTimeOffset to);

    [LoggerMessage(LogLevel.Information,
        "Dashboard admin {AdminDiscordUserId} removed excuse {ExcuseId}.")]
    partial void LogExcuseRemoved(ulong adminDiscordUserId, Guid excuseId);

    [LoggerMessage(LogLevel.Information,
        "Dashboard admin {AdminDiscordUserId} completed the account linking of Discord user {DiscordUserId} to GeoGuessr account {GeoGuessrUserId}.")]
    partial void LogLinkCompleted(ulong adminDiscordUserId, ulong discordUserId, string geoGuessrUserId);

    [LoggerMessage(LogLevel.Information,
        "Dashboard admin {AdminDiscordUserId} cancelled the linking request of Discord user {DiscordUserId} for GeoGuessr account {GeoGuessrUserId}.")]
    partial void LogLinkCancelled(ulong adminDiscordUserId, ulong discordUserId, string geoGuessrUserId);

    [LoggerMessage(LogLevel.Information,
        "Dashboard admin {AdminDiscordUserId} unlinked Discord user {DiscordUserId} from GeoGuessr account {GeoGuessrUserId}.")]
    partial void LogUnlinked(ulong adminDiscordUserId, ulong discordUserId, string geoGuessrUserId);
}

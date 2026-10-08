using GeoClubBot.DTOs;
using GeoClubBot.DTOs.Assemblers;
using GeoClubBot.Middleware;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UseCases.UseCases.Website;

namespace GeoClubBot.Controllers;

/// <summary>
/// The club website's numbers: both clubs' GeoGuessr figures and the Discord online count. Public;
/// 404 unless <c>Website:Enabled</c>.
/// </summary>
[ApiController]
[Route("/api/v1/stats")]
public class StatsController : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.WebsiteStats)]
    public async Task<ActionResult<WebsiteStatsDto>> ReadStats(ISender mediator, CancellationToken cancellationToken)
    {
        var stats = await mediator.Send(new GetWebsiteStatsQuery(), cancellationToken).ConfigureAwait(false);
        if (stats.IsFailure)
        {
            return this.ToProblemDetails(stats.Error);
        }

        // Set on every answer rather than by the CORS middleware, which only answers requests that
        // carry an Origin header: a CDN could otherwise cache a copy without it and hand that to
        // browsers. The data is public, so "*" is fine.
        Response.Headers.AccessControlAllowOrigin = "*";
        Response.Headers.CacheControl = "public, max-age=30, stale-while-revalidate=120";

        return Ok(WebsiteStatsDtoAssembler.AssembleDto(stats.Value));
    }
}

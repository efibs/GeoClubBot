using Configuration;
using GeoClubBot.DTOs;
using GeoClubBot.DTOs.Assemblers;
using GeoClubBot.Middleware;
using Infrastructure.OutputAdapters.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace GeoClubBot.Controllers;

[ApiController]
[Route("/api/v1/club")]
public class ClubController(IOptions<GeoGuessrConfiguration> geoGuessrConfig) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ClubDto>> ReadClub(IClubRepository clubRepository, CancellationToken cancellationToken)
    {
        var club = await clubRepository.ReadClubByIdAsync(_clubId).ConfigureAwait(false);

        var result = club is null
            ? Result<ClubDto>.Failure(Error.NotFound("club.not_found", $"Club with id {_clubId} was not found."))
            : Result<ClubDto>.Success(ClubDtoAssembler.AssembleDto(club));

        return result.ToActionResult(this);
    }

#if DEBUG
    [HttpPost("clubLevelUpEvent")]
    public async Task<IActionResult> TriggerClubLevelUp(int newLevel,
        IHubContext<ClubNotificationHub, IClubNotificationClient> hubContext,
        CancellationToken cancellationToken)
    {
        await hubContext.Clients.All.ClubLevelUp(newLevel).ConfigureAwait(false);

        return Accepted();
    }
#endif

    private readonly Guid _clubId = geoGuessrConfig.Value.MainClub.ClubId;
}

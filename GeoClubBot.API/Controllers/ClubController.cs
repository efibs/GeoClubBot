using Constants;
using GeoClubBot.DTOs;
using GeoClubBot.DTOs.Assemblers;
using GeoClubBot.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using UseCases.OutputPorts;

namespace GeoClubBot.Controllers;

[ApiController]
[Route("/club")]
public class ClubController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ClubDto>> ReadClub(IClubRepository clubRepository, CancellationToken cancellationToken)
    {
        try
        {
            // Read the club
            var club = await clubRepository.ReadClubByIdAsync(_clubId);
            
            // If the club was not found
            if (club == null)
            {
                return NotFound();
            }
            
            // Assemble the dto
            var dto = ClubDtoAssembler.AssembleDto(club);
            
            return Ok(dto);
        }
        catch (Exception ex)
        {
#if DEBUG
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message + "\n" + ex.StackTrace);
#else
            return StatusCode(StatusCodes.Status500InternalServerError);
#endif
        }
    }
    
#if DEBUG
    [HttpPost("clubLevelUpEvent")]
    public async Task<IActionResult> TriggerClubLevelUp(int newLevel, 
        IHubContext<ClubNotificationHub, IClubNotificationClient> hubContext,
        CancellationToken cancellationToken)
    {
        try
        {
            // Send the event
            await hubContext.Clients.All.ClubLevelUp(newLevel);
            
            return Accepted();
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message + "\n" + ex.StackTrace);
        }
    }
#endif 
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}
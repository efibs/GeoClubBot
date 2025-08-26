using Constants;
using GeoClubBot.DTOs;
using GeoClubBot.DTOs.Assemblers;
using Microsoft.AspNetCore.Mvc;
using UseCases.OutputPorts;

namespace GeoClubBot.Controllers;

[ApiController]
[Route("/club")]
public class ClubController(IClubRepository clubRepository, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ClubDto>> ReadClub(CancellationToken cancellationToken)
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
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
}
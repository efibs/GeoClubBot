namespace Entities;

public class GeoGuessrAccountLinkingRequest
{
    public required ulong DiscordUserId { get; set; }
    
    public required string GeoGuessrUserId { get; set; }
    
    public required string OneTimePassword { get; set; }
}
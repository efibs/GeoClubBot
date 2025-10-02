namespace Entities;

public sealed class GeoGuessrUser
{
    public required string UserId { get; set; }
    
    public required string Nickname { get; set; }

    public ulong? DiscordUserId { get; set; }
    
    public override string ToString()
    {
        return Nickname;
    }
}
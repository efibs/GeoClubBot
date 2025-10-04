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

    /// <summary>
    /// Create a deep copy of this user
    /// </summary>
    /// <returns></returns>
    public GeoGuessrUser DeepCopy()
    {
        return new GeoGuessrUser
        {
            UserId = UserId,
            Nickname = Nickname,
            DiscordUserId = DiscordUserId
        };
    }
}
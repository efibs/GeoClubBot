namespace Entities;

public sealed class GeoGuessrUser : BaseEntity
{
    public required string UserId { get; set; }
    
    public required string Nickname { get; set; }

    public ulong? DiscordUserId { get; set; }
    
    public override string ToString()
    {
        return Nickname;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not GeoGuessrUser other) return false;
        return UserId == other.UserId &&
               Nickname == other.Nickname &&
               DiscordUserId == other.DiscordUserId;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(UserId, Nickname, DiscordUserId);
    }

    public static bool operator ==(GeoGuessrUser? left, GeoGuessrUser? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(GeoGuessrUser? left, GeoGuessrUser? right) => !(left == right);
}
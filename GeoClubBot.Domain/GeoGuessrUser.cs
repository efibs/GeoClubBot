using Entities.Events;

namespace Entities;

public sealed class GeoGuessrUser : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public string Nickname { get; private set; } = string.Empty;

    public ulong? DiscordUserId { get; private set; }

    public static GeoGuessrUser Create(string userId, string nickname, ulong? discordUserId = null)
    {
        var user = new GeoGuessrUser
        {
            UserId = userId,
            Nickname = nickname,
            DiscordUserId = discordUserId
        };
        user.AddDomainEvent(new UserCreatedEvent(userId, nickname));
        return user;
    }

    public bool UpdateFromApi(string newNickname)
    {
        if (Nickname == newNickname)
        {
            return false;
        }

        var oldNickname = Nickname;
        Nickname = newNickname;
        AddDomainEvent(new UserUpdatedEvent(UserId, oldNickname, newNickname, DiscordUserId, DiscordUserId));
        return true;
    }

    public void LinkDiscord(ulong discordUserId)
    {
        DiscordUserId = discordUserId;
        AddDomainEvent(new AccountLinkedEvent(UserId, Nickname, discordUserId));
    }

    public void UnlinkDiscord()
    {
        if (DiscordUserId is null)
        {
            return;
        }

        var oldDiscordUserId = DiscordUserId.Value;
        DiscordUserId = null;
        AddDomainEvent(new AccountUnlinkedEvent(UserId, Nickname, oldDiscordUserId));
    }

    private GeoGuessrUser()
    {
    }

    public override string ToString() => Nickname;
}

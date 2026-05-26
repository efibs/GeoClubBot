namespace Entities;

public class GeoGuessrAccountLinkingRequest : BaseEntity
{
    public ulong DiscordUserId { get; private set; }

    public string GeoGuessrUserId { get; private set; } = string.Empty;

    public string OneTimePassword { get; private set; } = string.Empty;

    public static GeoGuessrAccountLinkingRequest Create(
        ulong discordUserId,
        string geoGuessrUserId,
        string oneTimePassword)
    {
        return new GeoGuessrAccountLinkingRequest
        {
            DiscordUserId = discordUserId,
            GeoGuessrUserId = geoGuessrUserId,
            OneTimePassword = oneTimePassword
        };
    }

    public bool Matches(string oneTimePassword) => OneTimePassword == oneTimePassword;

    private GeoGuessrAccountLinkingRequest()
    {
    }
}

using System.Security.Claims;

namespace GeoClubBot.Authentication;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Reads the authenticated Discord user id set by <see cref="DiscordActivityAuthenticationHandler"/>,
    /// or <c>null</c> when the principal carries no (valid) Discord user id claim.
    /// </summary>
    public static ulong? GetDiscordUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(DiscordActivityAuthenticationHandler.DiscordUserIdClaimType);
        return ulong.TryParse(raw, out var userId) ? userId : null;
    }
}

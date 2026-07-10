using Microsoft.AspNetCore.Authorization;

namespace GeoClubBot.Authentication;

/// <summary>
/// Authorization requirement for the admin-only Club Dashboard Activity endpoints: the viewer must
/// hold the Discord Administrator permission in the configured guild. Evaluated per request by
/// <see cref="ActivityAdminAuthorizationHandler"/>, so a revoked admin loses access on their next
/// request (unlike a claim baked in at authentication time, which would inherit the token cache's
/// lifetime).
/// </summary>
public sealed class ActivityAdminRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "ActivityAdmin";
}

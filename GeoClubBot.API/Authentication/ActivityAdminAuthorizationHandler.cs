using Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Authentication;

/// <summary>
/// Grants <see cref="ActivityAdminRequirement"/> when the authenticated Discord user is an
/// Administrator of the configured guild. Fails closed: no Discord identity, unknown guild member,
/// or a disconnected gateway all deny.
///
/// For local development the same bypass triple-gate as the authentication handler applies, plus an
/// explicit opt-in: the host must run in Development, the viewer must be the configured
/// <see cref="DiscordActivityConfiguration.DevUserId"/>, and
/// <see cref="DiscordActivityConfiguration.DevUserIsAdmin"/> must be set.
/// </summary>
public sealed class ActivityAdminAuthorizationHandler(
    IDiscordMemberPermissionAccess permissions,
    IHostEnvironment environment,
    IOptions<DiscordActivityConfiguration> config)
    : AuthorizationHandler<ActivityAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActivityAdminRequirement requirement)
    {
        if (context.User.GetDiscordUserId() is not { } discordUserId)
        {
            return;
        }

        if (environment.IsDevelopment()
            && config.Value.DevUserId == discordUserId
            && config.Value.DevUserIsAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        if (await permissions.IsAdministratorAsync(discordUserId).ConfigureAwait(false))
        {
            context.Succeed(requirement);
        }
    }
}

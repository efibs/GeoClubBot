using Configuration;
using Constants;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public sealed record UnlinkAccountsCommand(ulong DiscordUserId, string GeoGuessrUserId) : ICommand<bool>;

public sealed class UnlinkAccountsHandler(
    IGeoGuessrUserRepository users,
    IDiscordServerRolesAccess rolesAccess,
    IConfiguration config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IRequestHandler<UnlinkAccountsCommand, bool>
{
    private readonly ulong _hasLinkedRoleId =
        config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingHasLinkedRoleIdConfigurationKey);

    public async Task<bool> Handle(UnlinkAccountsCommand request, CancellationToken cancellationToken)
    {
        var user = await users
            .ReadForUpdateByUserIdAsync(request.GeoGuessrUserId)
            .ConfigureAwait(false);

        if (user is null || user.DiscordUserId != request.DiscordUserId)
        {
            return false;
        }

        user.UnlinkDiscord();

        var rolesToRemove = new List<ulong> { _hasLinkedRoleId };
        rolesToRemove.AddRange(
            geoGuessrConfig.Value.Clubs
                .Where(c => c.RoleId.HasValue)
                .Select(c => c.RoleId!.Value));

        await rolesAccess
            .RemoveRolesFromUserAsync(request.DiscordUserId, rolesToRemove.ToArray())
            .ConfigureAwait(false);

        return true;
    }
}

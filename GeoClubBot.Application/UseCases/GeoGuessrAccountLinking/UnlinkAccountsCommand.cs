using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using Utilities;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public sealed record UnlinkAccountsCommand(ulong DiscordUserId, string GeoGuessrUserId) : ICommand<Result>;

public sealed class UnlinkAccountsHandler(
    IGeoGuessrUserRepository users,
    IDiscordServerRolesAccess rolesAccess,
    IOptions<GeoGuessrAccountLinkingConfiguration> accountLinkingOptions,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IRequestHandler<UnlinkAccountsCommand, Result>
{
    private readonly ulong _hasLinkedRoleId = accountLinkingOptions.Value.HasLinkedRoleId;

    public async Task<Result> Handle(UnlinkAccountsCommand request, CancellationToken cancellationToken)
    {
        var user = await users
            .ReadForUpdateByUserIdAsync(request.GeoGuessrUserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.DiscordUserId != request.DiscordUserId)
        {
            return Error.NotFound(
                "account_linking.not_linked",
                "The given accounts are not linked.");
        }

        user.UnlinkDiscord();

        var rolesToRemove = new List<ulong> { _hasLinkedRoleId };
        rolesToRemove.AddRange(
            geoGuessrConfig.Value.Clubs
                .Where(c => c.RoleId.HasValue)
                .Select(c => c.RoleId!.Value));

        await rolesAccess
            .RemoveRolesFromUserAsync(request.DiscordUserId, rolesToRemove.ToArray(), cancellationToken)
            .ConfigureAwait(false);

        return Result.Success();
    }
}

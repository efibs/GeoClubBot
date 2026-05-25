using Configuration;
using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class UnlinkAccountsUseCase(
    IUnitOfWork unitOfWork,
    IDiscordServerRolesAccess rolesAccess,
    IConfiguration config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IUnlinkAccountsUseCase
{
    public async Task<bool> UnlinkAccountsAsync(ulong discordUserId, string geoGuessrUserId)
    {
        var user = await unitOfWork.GeoGuessrUsers
            .ReadForUpdateByUserIdAsync(geoGuessrUserId)
            .ConfigureAwait(false);

        if (user is null || user.DiscordUserId != discordUserId)
        {
            return false;
        }

        user.UnlinkDiscord();

        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        var rolesToRemove = new List<ulong> { _hasLinkedRoleId };
        rolesToRemove.AddRange(
            geoGuessrConfig.Value.Clubs
                .Where(c => c.RoleId.HasValue)
                .Select(c => c.RoleId!.Value));

        await rolesAccess
            .RemoveRolesFromUserAsync(discordUserId, rolesToRemove.ToArray())
            .ConfigureAwait(false);

        return true;
    }

    private readonly ulong _hasLinkedRoleId =
        config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingHasLinkedRoleIdConfigurationKey);
}

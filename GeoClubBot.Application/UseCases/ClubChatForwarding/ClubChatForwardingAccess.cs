using Configuration;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.ClubChatForwarding;

/// <summary>Who may forward, and to which clubs: shared by the form and the send.</summary>
public sealed class ClubChatForwardingAccess(
    IDiscordMemberPermissionAccess permissions,
    IClubRepository clubs,
    IOptions<ClubChatForwardingConfiguration> config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig)
{
    public static readonly Error Disabled =
        Error.Forbidden("club-chat.disabled", "Forwarding to the club chat is switched off.");

    /// <summary>
    /// The command is hidden from non-administrators, but Discord lets server admins grant it to
    /// anyone, so the configured roles are checked here as well.
    /// </summary>
    public async Task<Result> CheckAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        if (!config.Value.Enabled)
        {
            return Disabled;
        }

        var allowed = await permissions
            .IsAdministratorOrHasAnyRoleAsync(discordUserId, config.Value.AdminRoleIds, cancellationToken)
            .ConfigureAwait(false);

        return allowed
            ? Result.Success()
            : Error.Forbidden("club-chat.not_admin", "Only admins can forward messages to the club chat.");
    }

    /// <summary>The configured clubs that take forwards, in configuration order, named as stored.</summary>
    public async Task<IReadOnlyList<ClubChatTarget>> ReadTargetsAsync(CancellationToken cancellationToken)
    {
        var stored = await clubs.ReadAllClubsAsync(cancellationToken).ConfigureAwait(false);
        var names = stored.ToDictionary(c => c.ClubId, c => c.Name);

        return geoGuessrConfig.Value.Clubs
            .Where(c => c.ForwardChat != false)
            .Select(c => new ClubChatTarget(c.ClubId, names.TryGetValue(c.ClubId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : c.ClubId.ToString()))
            .ToList();
    }
}

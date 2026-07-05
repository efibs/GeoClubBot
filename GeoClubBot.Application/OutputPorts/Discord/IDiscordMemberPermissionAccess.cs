namespace UseCases.OutputPorts.Discord;

public interface IDiscordMemberPermissionAccess
{
    /// <summary>
    /// Whether the Discord user holds the Administrator permission in the configured guild — the
    /// same gate the admin slash commands use via <c>[DefaultMemberPermissions(Administrator)]</c>.
    /// Returns <c>false</c> when the user is unknown, not a guild member, or the gateway is not
    /// connected (fail closed).
    /// </summary>
    Task<bool> IsAdministratorAsync(ulong discordUserId, CancellationToken cancellationToken = default);
}

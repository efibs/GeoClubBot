namespace UseCases.OutputPorts.Discord;

/// <summary>
/// Stable <see cref="Utilities.Error.Code"/> values returned by <see cref="IDiscordDirectMessageAccess"/>
/// so callers can react to specific Discord delivery failures without depending on Discord.Net types.
/// </summary>
public static class DiscordDmErrorCodes
{
    /// <summary>Discord 50007: the user has DMs from the bot disabled or has blocked it.</summary>
    public const string Disabled = "discord.dm.disabled";

    /// <summary>
    /// Discord 50278: the bot shares no mutual guild with the user, i.e. the user has left the
    /// server. Permanent — the bot can never DM them again, so any reminder should be deactivated.
    /// </summary>
    public const string NoMutualGuild = "discord.dm.no_mutual_guild";
}

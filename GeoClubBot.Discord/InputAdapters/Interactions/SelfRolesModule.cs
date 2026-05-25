using System.Text;
using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("self-roles", "Commands for managing self-roles")]
public class SelfRolesModule(ISender mediator, ILogger<SelfRolesModule> logger, IConfiguration config)
    : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("select", "Select the roles you would like to have")]
    public Task SelectSelfRolesSlashCommandAsync() =>
        ExecuteAsync(
            _ => _handleSelectSelfRolesStartedAsync(),
            ephemeral: true,
            failureMessage: "Failed to open self role selection. Please try again later. If the issue persists, please contact an admin.");

    [ComponentInteraction(ComponentIds.SelfRolesSelectButtonId, ignoreGroupNames: true)]
    public Task SelectSelfRolesButtonPressedAsync() =>
        ExecuteAsync(
            _ => _handleSelectSelfRolesStartedAsync(),
            ephemeral: true,
            failureMessage: "Failed to open self role selection. Please try again later. If the issue persists, please contact an admin.");

    [ComponentInteraction($"{ComponentIds.SelfRolesSelectMenuId}:*", ignoreGroupNames: true)]
    public Task HandleSelfRolesSelectionAsync(string userId, string[] selectedRoleIdStrings) =>
        ExecuteAsync(
            _ => _handleSelectionAsync(userId, selectedRoleIdStrings),
            ephemeral: true,
            failureMessage: "Failed to update self roles. Please try again later. If the issue persists, please contact an admin.");

    private async Task _handleSelectionAsync(string userId, string[] selectedRoleIdStrings)
    {
        if (Context.User.Id.ToString() != userId)
        {
            await FollowupAsync("You can’t confirm someone else’s roles.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var selectedRoleIds = selectedRoleIdStrings.Select(ulong.Parse).ToHashSet();

        var guildUser = (SocketGuildUser)Context.User;
        var guild = guildUser.Guild;

        var assignableRoles = guild.Roles.Where(r => _assignableRoleIds.Contains(r.Id)).ToList();
        var toAdd = assignableRoles.Where(r => selectedRoleIds.Contains(r.Id) && !guildUser.Roles.Contains(r)).ToList();
        var toRemove = assignableRoles.Where(r => !selectedRoleIds.Contains(r.Id) && guildUser.Roles.Contains(r)).ToList();

        foreach (var role in toAdd)
        {
            await guildUser.AddRoleAsync(role).ConfigureAwait(false);
        }

        foreach (var role in toRemove)
        {
            await guildUser.RemoveRoleAsync(role).ConfigureAwait(false);
        }

        var msgBuilder = new StringBuilder("## Updated your roles!\n");

        if (toAdd.Count > 0)
        {
            msgBuilder.Append("- Added: ");
            msgBuilder.AppendLine(string.Join(", ", toAdd.Select(r => r.Name)));
        }

        if (toRemove.Count > 0)
        {
            msgBuilder.Append("- Removed: ");
            msgBuilder.AppendLine(string.Join(", ", toRemove.Select(r => r.Name)));
        }

        await FollowupAsync(msgBuilder.ToString(), ephemeral: true).ConfigureAwait(false);
    }

    private async Task _handleSelectSelfRolesStartedAsync()
    {
        var user = Context.Interaction.User as SocketGuildUser
            ?? throw new InvalidOperationException("User is not a SocketGuildUser");

        var usersRoleIds = user.Roles.Select(x => x.Id).ToHashSet();

        var menu = new SelectMenuBuilder()
            .WithCustomId($"{ComponentIds.SelfRolesSelectMenuId}:{user.Id}")
            .WithPlaceholder("Select your roles...")
            .WithMinValues(0)
            .WithMaxValues(_selfRoleSettings.Count);

        foreach (var roleSetting in _selfRoleSettings)
        {
            var playerAlreadyHasRole = usersRoleIds.Contains(roleSetting.RoleId);

            var role = await Context.Guild.GetRoleAsync(roleSetting.RoleId).ConfigureAwait(false);

            var emote = string.IsNullOrWhiteSpace(roleSetting.RoleEmoji) ? null : Emoji.Parse(roleSetting.RoleEmoji);

            menu.AddOption(new SelectMenuOptionBuilder(
                role.Name,
                role.Id.ToString(),
                description: roleSetting.RoleDescription,
                isDefault: playerAlreadyHasRole,
                emote: emote));
        }

        var component = new ComponentBuilder()
            .WithSelectMenu(menu);

        await FollowupAsync("## Select your roles below:", components: component.Build(), ephemeral: true).ConfigureAwait(false);
    }

    private readonly List<SelfRoleSetting> _selfRoleSettings = config
        .GetSection(ConfigKeys.SelfRolesRolesConfigurationKey)
        .Get<List<SelfRoleSetting>>()!;

    private readonly HashSet<ulong> _assignableRoleIds = config
        .GetSection(ConfigKeys.SelfRolesRolesConfigurationKey)
        .Get<List<SelfRoleSetting>>()!
        .Select(rs => rs.RoleId)
        .ToHashSet();
}

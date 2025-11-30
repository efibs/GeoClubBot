using System.Text;
using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("self-roles", "Commands for managing self-roles")]
public class SelfRolesModule : InteractionModuleBase<SocketInteractionContext>
{
    public SelfRolesModule(ILogger<SelfRolesModule> logger, IConfiguration config)
    {
        _selfRoleSettings = config
            .GetSection(ConfigKeys.SelfRolesRolesConfigurationKey)
            .Get<List<SelfRoleSetting>>()!;
        _assignableRoleIds = _selfRoleSettings.Select(rs => rs.RoleId).ToHashSet();
        _logger = logger;
    }
    
    [SlashCommand("select", "Select the roles you would like to have")]
    public async Task SelectSelfRolesSlashCommandAsync()
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            // Handle
            await _handleSelectSelfRolesStartedAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Log the error
            _logger.LogError(e, "Error while trying to send self roles selection.");
            
            // Respond
            await FollowupAsync("Failed to open self role selection. Please try again later. If the issue persists, please contact an admin.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }
    
    [ComponentInteraction(ComponentIds.SelfRolesSelectButtonId, ignoreGroupNames: true)]
    public async Task SelectSelfRolesButtonPressedAsync()
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            // Handle
            await _handleSelectSelfRolesStartedAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Log the error
            _logger.LogError(e, "Error while trying to send self roles selection.");
            
            // Respond
            await FollowupAsync("Failed to open self role selection. Please try again later. If the issue persists, please contact an admin.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    [ComponentInteraction($"{ComponentIds.SelfRolesSelectMenuId}:*", ignoreGroupNames: true)]
    public async Task HandleSelfRolesSelectionAsync(string userId, string[] selectedRoleIdStrings)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);
            
            if (Context.User.Id.ToString() != userId)
            {
                await FollowupAsync("You can’t confirm someone else’s roles.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Parse to ulong
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

            // Build the response message
            var msgBuilder = new StringBuilder("## Updated your roles!\n");
            
            // If there are added roles
            if (toAdd.Count > 0)
            {
                msgBuilder.Append("- Added: ");
                msgBuilder.AppendLine(string.Join(", ", toAdd.Select(r => r.Name)));
            }
            
            // If there are removed roles
            if (toRemove.Count > 0)
            {
                msgBuilder.Append("- Removed: ");
                msgBuilder.AppendLine(string.Join(", ", toRemove.Select(r => r.Name)));
            }
            
            await FollowupAsync(
                    msgBuilder.ToString(), 
                    ephemeral: true)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Log the error
            _logger.LogError(e, "Error while trying to update self roles.");
            
            // Respond
            await FollowupAsync("Failed to update self roles. Please try again later. If the issue persists, please contact an admin.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }
    
    private async Task _handleSelectSelfRolesStartedAsync()
    {
        // Get the user that clicked the button as a guild user
        var user = Context.Interaction.User as SocketGuildUser;
        
        // If the user is not a guild user
        if (user == null)
        {
            throw new InvalidOperationException("User is not a SocketGuildUser");
        }
        
        // Get the users roles ids
        var usersRoleIds = user.Roles.Select(x => x.Id).ToHashSet();
        
        // Create the select menu builder
        var menu = new SelectMenuBuilder()
            .WithCustomId($"{ComponentIds.SelfRolesSelectMenuId}:{user.Id}")
            .WithPlaceholder("Select your roles...")
            .WithMinValues(0)
            .WithMaxValues(_selfRoleSettings.Count);

        // For every self role
        foreach (var roleSetting in _selfRoleSettings)
        {
            // Get if the player already has the role
            var playerAlreadyHasRole = usersRoleIds.Contains(roleSetting.RoleId);
            
            // Get the role
            var role = await Context.Guild.GetRoleAsync(roleSetting.RoleId).ConfigureAwait(false);
            
            // Get the emote
            var emote = string.IsNullOrWhiteSpace(roleSetting.RoleEmoji) ? null : Emoji.Parse(roleSetting.RoleEmoji);
            
            // Add a new menu entry
            menu.AddOption(new SelectMenuOptionBuilder(
                role.Name, 
                role.Id.ToString(), 
                description: roleSetting.RoleDescription, 
                isDefault: playerAlreadyHasRole,
                emote: emote));
        }
        
        // Build the component
        var component = new ComponentBuilder()
            .WithSelectMenu(menu);
        
        // Send the selection
        await FollowupAsync("## Select your roles below:", components: component.Build(), ephemeral: true).ConfigureAwait(false);
    }
    
    private readonly List<SelfRoleSetting> _selfRoleSettings;
    private readonly HashSet<ulong> _assignableRoleIds;
    private readonly ILogger<SelfRolesModule> _logger;
}
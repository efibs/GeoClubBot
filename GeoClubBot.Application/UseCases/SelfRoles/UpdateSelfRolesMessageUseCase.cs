using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.SelfRoles;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.SelfRoles;

public class UpdateSelfRolesMessageUseCase(
    IDiscordTextChannelAccess discordTextChannelAccess,
    IDiscordMessageAccess discordMessageAccess,
    IDiscordSelfUserAccess discordSelfUserAccess,
    IConfiguration config) : IUpdateSelfRolesMessageUseCase
{
    public async Task UpdateSelfRolesMessageAsync()
    {
        // Get the own user id
        var ownUserId = discordSelfUserAccess.GetSelfUserId();
        
        // Try to get the old message
        var oldMessageId = await discordTextChannelAccess
            .ReadLastMessageOfUserAsync(ownUserId, _textChannelId, 100)
            .ConfigureAwait(false);
        
        // Get if an old message exists
        var oldMessageExists = oldMessageId != null;
        
        // Get if there are self roles configured
        var selfRolesConfigured = _selfRoleSettings.Count > 0;
        
        // If there is no old message and there are self roles
        if (oldMessageExists == false && selfRolesConfigured)
        {
            // Create the self role message
            await discordMessageAccess.SendSelfRolesMessageAsync(_textChannelId, _selfRoleSettings).ConfigureAwait(false);
        }
        // Else if there is an old message and there are self roles
        else if (oldMessageExists && selfRolesConfigured)
        {
            // Update the self role message
            await discordMessageAccess.UpdateSelfRolesMessageAsync(_textChannelId,oldMessageId!.Value, _selfRoleSettings).ConfigureAwait(false);
        }
        // Else if there is an old message and there are now no self roles configured
        else if (oldMessageExists && selfRolesConfigured == false)
        {
            // Delete the self role message
            await discordMessageAccess.DeleteMessageAsync(oldMessageId!.Value, _textChannelId).ConfigureAwait(false);
        }
    }
    
    private readonly ulong _textChannelId = config.GetValue<ulong>(ConfigKeys.SelfRolesTextChannelIdConfigurationKey);
    private readonly List<SelfRoleSetting> _selfRoleSettings = config
        .GetSection(ConfigKeys.SelfRolesRolesConfigurationKey)
        .Get<List<SelfRoleSetting>>()!;
}
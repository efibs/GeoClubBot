using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts.SelfRoles;
using UseCases.OutputPorts;

namespace UseCases.UseCases.SelfRoles;

public class UpdateSelfRolesMessageUseCase(
    ITextChannelAccess textChannelAccess,
    IMessageAccess messageAccess,
    ISelfUserAccess selfUserAccess,
    IConfiguration config) : IUpdateSelfRolesMessageUseCase
{
    public async Task UpdateSelfRolesMessageAsync()
    {
        // Get the own user id
        var ownUserId = selfUserAccess.GetSelfUserId();
        
        // Try to get the old message
        var oldMessageId = await textChannelAccess
            .ReadLastMessageOfUserAsync(ownUserId, _textChannelId, 100)
            .ConfigureAwait(false);
        
        // If there is an old message
        if (oldMessageId != null)
        {
            // Delete the old message
            await messageAccess.DeleteMessageAsync(oldMessageId.Value, _textChannelId).ConfigureAwait(false);
        }
        
        // Create the new message
        await messageAccess.SendSelfRolesMessageAsync(_textChannelId, _selfRoleSettings).ConfigureAwait(false);
    }
    
    private readonly ulong _textChannelId = config.GetValue<ulong>(ConfigKeys.SelfRolesTextChannelIdConfigurationKey);
    private readonly IEnumerable<SelfRoleSetting> _selfRoleSettings = config
        .GetSection(ConfigKeys.SelfRolesRolesConfigurationKey)
        .Get<List<SelfRoleSetting>>()!;
}
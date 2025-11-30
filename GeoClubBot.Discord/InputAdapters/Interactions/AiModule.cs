using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.AI;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[Group("ai", "Commands for controlling the AI stuff")]
public class AiModule(IServiceProvider serviceProvider, ILogger<AiModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("rebuild-plonkit-guide", "Rebuilds the internal PlonkIt Guide clone")]
    public async Task RebuildPlonkItGuideVectorStore()
    {
        // If the AI features are not active
        if (_plonkItGuideVectorStore == null)
        {
            await RespondAsync("AI features are not active.", ephemeral: true).ConfigureAwait(false);
            return;
        }
        
        try
        {
            // Defer the response
            await DeferAsync().ConfigureAwait(false);
            
            // Rebuild
            var statusUpdates = _plonkItGuideVectorStore.RebuildStoreAsync();

            var index = 0;
            
            // For every status update
            await foreach (var statusUpdate in statusUpdates.ConfigureAwait(false))
            {
                if (index++ % 10 == 0)
                {
                    // Update the status 
                    await ModifyOriginalResponseAsync(msg => msg.Content = statusUpdate).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            // Log error
            logger.LogError(ex, "Failed to rebuild the internal PlonkIt Guide clone");
            
            // Respond
            await FollowupAsync("Failed to rebuild the internal PlonkIt Guide clone.", ephemeral: true).ConfigureAwait(false);
        }
    }
    
    private readonly PlonkItGuideVectorStore? _plonkItGuideVectorStore = serviceProvider.GetService<PlonkItGuideVectorStore>();
}
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.AI;

namespace Infrastructure.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[Group("ai", "Commands for controlling the AI stuff")]
public class AiModule(PlonkItGuideVectorStore plonkItGuideVectorStore,
    ILogger<AiModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("rebuild-plonkit-guide", "Rebuilds the internal PlonkIt Guide clone")]
    public async Task RebuildPlonkItGuideVectorStore()
    {
        try
        {
            // Defer the response
            await DeferAsync().ConfigureAwait(false);
            
            // Rebuild
            var statusUpdates = plonkItGuideVectorStore.RebuildStoreAsync();

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
}
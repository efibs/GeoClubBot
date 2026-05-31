using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[Group("ai", "Commands for controlling the AI stuff")]
public class AiModule(IServiceProvider serviceProvider, ISender mediator, ILogger<AiModule> logger)
    : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("rebuild-plonkit-guide", "Rebuilds the internal PlonkIt Guide clone")]
    public async Task RebuildPlonkItGuideVectorStore()
    {
        if (_plonkItGuideVectorStore == null)
        {
            await RespondAsync("AI features are not active.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await ExecuteAsync(
            async ct =>
            {
                var statusUpdates = _plonkItGuideVectorStore.RebuildStoreAsync(ct);

                var index = 0;
                await foreach (var statusUpdate in statusUpdates.ConfigureAwait(false))
                {
                    if (index++ % 10 == 0)
                    {
                        await ModifyOriginalResponseAsync(msg => msg.Content = statusUpdate).ConfigureAwait(false);
                    }
                }
            },
            failureMessage: "Failed to rebuild the internal PlonkIt Guide clone.");
    }

    private readonly IPlonkItGuideVectorStore? _plonkItGuideVectorStore = serviceProvider.GetService<IPlonkItGuideVectorStore>();
}

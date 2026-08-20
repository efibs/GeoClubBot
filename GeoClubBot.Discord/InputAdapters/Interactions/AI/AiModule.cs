using System.Globalization;
using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

[CommandContextType(InteractionContextType.Guild)]
[Group("ai", "Commands for controlling the AI features")]
public class AiModule(IServiceProvider serviceProvider, ISender mediator, ILogger<AiModule> logger)
    : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("status", "Show which AI models are available and how much budget is left today")]
    public Task StatusAsync() =>
        ExecuteAsync(async _ =>
        {
            if (_catalog is null || _knowledgeIndex is null)
            {
                await FollowupAsync("AI features are not active.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var status = _catalog.ReadStatus();
            var chain = await _catalog.ReadChainAsync(new ChatModelRequirements()).ConfigureAwait(false);

            var indexedChunks = await ReadIndexSizeAsync().ConfigureAwait(false);

            var embed = new EmbedBuilder()
                .WithTitle("🤖 AI status")
                .AddField("Free models known", $"{status.ModelCount} ({status.VisionModelCount} accept images)", inline: true)
                .AddField("Catalog source", status.Source.ToString(), inline: true)
                .AddField("Last refreshed", status.LastRefreshedAtUtc is { } at
                    ? TimestampTag.FromDateTimeOffset(at, TimestampTagStyles.Relative).ToString()
                    : "never", inline: true)
                .AddField("Model chain", $"`{string.Join("` → `", chain)}`")
                .AddField("Indexed guide chunks", indexedChunks, inline: true)
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to read the AI status.");

    private async Task<string> ReadIndexSizeAsync()
    {
        try
        {
            var count = await _knowledgeIndex!.CountAsync().ConfigureAwait(false);
            return count.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            // The vector store being unreachable is itself useful status, not a reason to fail the
            // whole command.
            return "unavailable";
        }
    }

    // Resolved optionally so the module still loads when the AI feature is switched off.
    private readonly IChatModelCatalog? _catalog = serviceProvider.GetService<IChatModelCatalog>();

    private readonly IKnowledgeIndex? _knowledgeIndex = serviceProvider.GetService<IKnowledgeIndex>();
}

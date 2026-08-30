using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Ingestion;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

[CommandContextType(InteractionContextType.Guild)]
[Group("ai", "Commands for controlling the AI features")]
public class AiModule(IServiceProvider serviceProvider, ISender mediator, ILogger<AiModule> logger)
    : ClubBotInteractionModule(mediator, logger)
{
    /// <summary>Hits shown by /ai search; enough to judge retrieval without flooding the reply.</summary>
    private const int SearchResultLimit = 5;

    [SlashCommand("status", "Show which AI models are available, how much is indexed, and today's budget")]
    public Task StatusAsync() =>
        ExecuteAsync(async ct =>
        {
            if (_catalog is null || _knowledgeIndex is null)
            {
                await FollowupAsync("AI features are not active.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var status = _catalog.ReadStatus();
            var chain = await _catalog.ReadChainAsync(new ChatModelRequirements(), ct).ConfigureAwait(false);
            var sources = await Mediator.Send(new ReadKnowledgeSourceStatusQuery(), ct).ConfigureAwait(false);

            var embed = new EmbedBuilder()
                .WithTitle("🤖 AI status")
                .AddField("Free models known", $"{status.ModelCount} ({status.VisionModelCount} accept images)", inline: true)
                .AddField("Catalog", status.Source.ToString(), inline: true)
                .AddField("Refreshed", status.LastRefreshedAtUtc is { } at
                    ? TimestampTag.FromDateTimeOffset(at, TimestampTagStyles.Relative).ToString()
                    : "never", inline: true)
                .AddField("Model chain", $"`{string.Join("` → `", chain)}`")
                .AddField("Indexed chunks", await ReadIndexSizeAsync(ct).ConfigureAwait(false), inline: true)
                .AddField("Sources", sources switch
                {
                    { IsFailure: true } => "unavailable",
                    { Value.Total: 0 } => "none catalogued — run `/ai sync-sources`",
                    var ok => $"{ok.Value.Ingested} indexed · {ok.Value.Pending} pending · " +
                              $"{ok.Value.Failed} failed · {ok.Value.Skipped} skipped"
                })
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to read the AI status.");

    [SlashCommand("search", "Show what the guide index returns for a query, without asking a model")]
    public Task SearchAsync(
        [Summary(description: "What to look for")] string query,
        [Summary(description: "Restrict to one country")] string? country = null) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new SearchKnowledgeQuery(query, country, SearchResultLimit), ct)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            if (result.Value.Count == 0)
            {
                await FollowupAsync("Nothing in the index matched that.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(FormatHits(result.Value), ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to search the guide index.");

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("sync-sources", "Refresh the catalogue of known guide sources")]
    public Task SyncSourcesAsync() =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new SyncSourceCatalogsCommand(), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            var report = result.Value;
            await FollowupAsync(
                    $"Catalogue synced: **{report.Discovered}** listed, **{report.Added}** new, " +
                    $"**{report.Updated}** refreshed, **{report.Tombstoned}** no longer listed.",
                    ephemeral: true)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to sync the source catalogue.");

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("ingest", "Index a batch of due guide sources now")]
    public Task IngestAsync(
        [Summary(description: "How many sources to process")] int count = 5,
        [Summary(description: "Restrict to one source type, e.g. plonkit")] string? sourceType = null,
        [Summary(description: "Re-index even if the content is unchanged")] bool force = false) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new IngestKnowledgeSourcesCommand(count, sourceType, force), ct)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            var report = result.Value;

            // A run that attempts nothing is the common first-time case, and four zeros explain
            // none of it: the queue is empty because nothing has been catalogued, or because
            // everything catalogued was indexed recently and is not due again yet.
            if (report.Attempted == 0 && !report.BudgetExhausted)
            {
                await FollowupAsync(await DescribeEmptyRunAsync(ct).ConfigureAwait(false), ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            var message = new StringBuilder()
                .AppendLine($"Indexed **{report.Ingested}** source(s), **{report.ChunksWritten}** chunk(s).")
                .AppendLine($"Unchanged: {report.Unchanged} · Failed: {report.Failed} · Skipped: {report.Skipped}");

            if (report.BudgetExhausted)
            {
                message.AppendLine("\n⚠️ Stopped early — indexing has used its share of today's AI allowance.");
            }

            await FollowupAsync(message.ToString(), ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to index guide sources.");

    /// <summary>
    /// Explains a run that indexed nothing. Which of the two reasons applies is the difference
    /// between "you have not started yet" and "there is nothing left to do", and the operator cannot
    /// tell them apart from the counts alone.
    /// </summary>
    private async Task<string> DescribeEmptyRunAsync(CancellationToken cancellationToken)
    {
        var sources = await Mediator.Send(new ReadKnowledgeSourceStatusQuery(), cancellationToken)
            .ConfigureAwait(false);

        if (sources.IsFailure || sources.Value.Total == 0)
        {
            return "Nothing to index yet — no guide sources are catalogued.\n"
                   + "Run `/ai sync-sources` first to discover them.";
        }

        var counts = sources.Value;
        return $"Nothing was due for indexing. {counts.Ingested} source(s) are already indexed, "
               + $"{counts.Pending} pending, {counts.Failed} failed, {counts.Skipped} skipped.\n"
               + "Already-indexed sources are left alone until their re-index interval passes; "
               + "use `force: true` to re-index them now.";
    }

    /// <summary>Compact so several hits fit one ephemeral reply and can be judged at a glance.</summary>
    private static string FormatHits(IReadOnlyList<KnowledgeHit> hits)
    {
        var builder = new StringBuilder();

        foreach (var hit in hits)
        {
            var kind = hit.Kind == KnowledgeChunkKind.Image ? "🖼️" : "📄";
            var snippet = hit.Text.Length > 160 ? hit.Text[..160] + "…" : hit.Text;

            builder
                .AppendLine($"{kind} **{hit.Score:F3}** · {hit.SectionPath ?? hit.Title ?? "(untitled)"}")
                .AppendLine($"{snippet.ReplaceLineEndings(" ")}")
                .AppendLine($"<{hit.SourceUrl}>")
                .AppendLine();
        }

        return builder.ToString();
    }

    private async Task<string> ReadIndexSizeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var count = await _knowledgeIndex!.CountAsync(cancellationToken).ConfigureAwait(false);
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

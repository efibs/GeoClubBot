using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Feedback;
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

            // Started first, because it may wait out its timeout and nothing else here depends on it.
            var pictureLinks = _imageRelay?.CheckPublicReachabilityAsync(ct);

            var status = _catalog.ReadStatus();
            var chain = await _catalog.ReadChainAsync(new ChatModelRequirements(), ct).ConfigureAwait(false);
            var sources = await Mediator.Send(new ReadKnowledgeSourceStatusQuery(), ct).ConfigureAwait(false);

            var builder = new EmbedBuilder()
                .WithTitle("🤖 AI status")
                .AddField("Free models known", $"{status.ModelCount} ({status.VisionModelCount} accept images)", inline: true)
                .AddField("Catalog", status.Source.ToString(), inline: true)
                .AddField("Refreshed", status.LastRefreshedAtUtc is { } at
                    ? TimestampTag.FromDateTimeOffset(at, TimestampTagStyles.Relative).ToString()
                    : "never", inline: true)
                .AddField("Model chain", $"`{string.Join("` → `", chain)}`");

            // Shown so a runtime exclusion is noticed without reading logs: it is forgotten on restart,
            // and each one is a candidate for the configured block list. Capped well inside Discord's
            // 1024-character field limit; a longer list is in the logs.
            if (status.LearnedUnfitModelIds.Count > 0)
            {
                const int maxListed = 8;
                var listed = string.Join("`, `", status.LearnedUnfitModelIds.Take(maxListed));
                var more = status.LearnedUnfitModelIds.Count > maxListed
                    ? $" and {status.LearnedUnfitModelIds.Count - maxListed} more"
                    : string.Empty;

                builder.AddField("Excluded until restart",
                    $"`{listed}`{more} — add to `AI:OpenRouter:BlockedModelIds` to keep them out");
            }

            var embed = builder
                .AddField("Indexed chunks", await ReadIndexSizeAsync(ct).ConfigureAwait(false), inline: true)
                .AddField("Sources", sources switch
                {
                    { IsFailure: true } => "unavailable",
                    { Value.Total: 0 } => "none catalogued — run `/ai sync-sources`",
                    var ok => AiSourcesFormatter.DescribeCounts(ok.Value)
                })
                .AddField(AiImageRelayStatusFormatter.FieldName, AiImageRelayStatusFormatter.Describe(
                    pictureLinks is null ? null : await pictureLinks.ConfigureAwait(false)))
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
    [SlashCommand("feedback", "Show how people have rated my answers")]
    public Task FeedbackAsync(
        [Summary(description: "Days to count over; 0 counts everything")] int days = 30) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new ReadAiFeedbackSummaryQuery(days), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(embed: AiFeedbackFormatter.BuildSummaryEmbed(result.Value, days), ephemeral: true)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to read the feedback summary.");

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("feedback-export", "Download the rated conversations as JSONL")]
    public Task FeedbackExportAsync(
        [Summary(description: "Only good or only bad answers")]
        [Choice("good", "positive")] [Choice("bad", "negative")] string? rating = null,
        [Summary(description: "Days to export; 0 exports everything")] int days = 90) =>
        ExecuteAsync(async ct =>
        {
            AiFeedbackRating? wanted = rating switch
            {
                "positive" => AiFeedbackRating.Positive,
                "negative" => AiFeedbackRating.Negative,
                _ => null
            };

            var result = await Mediator.Send(new ExportAiFeedbackQuery(wanted, days, Limit: null), ct)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            if (result.Value.Count == 0)
            {
                await FollowupAsync("Nothing has been rated in that window yet.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            // Written to a stream rather than a message: a single archived conversation already
            // exceeds Discord's 2000-character message limit.
            using var stream = new MemoryStream(
                Encoding.UTF8.GetBytes(AiFeedbackFormatter.RenderJsonLines(result.Value)));

            var fileName = $"ai-feedback-{DateOnly.FromDateTime(DateTime.UtcNow):yyyyMMdd}.jsonl";

            await FollowupWithFileAsync(stream, fileName,
                    text: $"**{result.Value.Count}** rated conversation(s), newest first.",
                    ephemeral: true)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to export the feedback archive.");

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
            if (report is { Attempted: 0, BudgetExhausted: false, RateLimited: false, HostRefused: 0, Removed: 0 })
            {
                await FollowupAsync(await DescribeEmptyRunAsync(ct).ConfigureAwait(false), ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            await FollowupAsync(AiSourcesFormatter.DescribeRun(report), ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to index guide sources.");

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("backfill-images", "Queue indexed guides whose images were never embedded")]
    public Task BackfillImagesAsync() =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new BackfillMissingImagesCommand(), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            var report = result.Value;
            var message = report.SourcesWithMissingImages == 0
                ? "Every indexed image has been embedded — nothing to backfill."
                : $"Queued **{report.Queued}** of **{report.SourcesWithMissingImages}** source(s) with missing "
                  + "images. The nightly runs pick them up, or `/ai ingest` does sooner.";

            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to queue the image backfill.");

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("sources", "List guide sources that failed, were skipped or are unavailable, and why")]
    public Task SourcesAsync(
        [Summary(description: "Which sources to list")]
        [Choice("needing a look", nameof(KnowledgeSourceListKind.Problems))]
        [Choice("failed", nameof(KnowledgeSourceListKind.Failed))]
        [Choice("skipped", nameof(KnowledgeSourceListKind.Skipped))]
        [Choice("unavailable", nameof(KnowledgeSourceListKind.Unavailable))]
        [Choice("pending", nameof(KnowledgeSourceListKind.Pending))]
        [Choice("no longer listed", nameof(KnowledgeSourceListKind.Removed))]
        string status = nameof(KnowledgeSourceListKind.Problems),
        [Summary(description: "Restrict to one source type, e.g. gdoc")] string? sourceType = null) =>
        ExecuteAsync(async ct =>
        {
            var kind = Enum.TryParse<KnowledgeSourceListKind>(status, out var parsed)
                ? parsed
                : KnowledgeSourceListKind.Problems;

            var result = await Mediator.Send(new ListKnowledgeSourcesQuery(kind, sourceType), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(embed: AiSourcesFormatter.BuildListEmbed(result.Value, kind, sourceType), ephemeral: true)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to list the guide sources.");

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("source-retry", "Queue failed, skipped or unavailable guide sources to be tried again")]
    public Task SourceRetryAsync(
        [Summary(description: "One source, by the key or link /ai sources shows")] string? key = null,
        [Summary(description: "Every such source of one type, e.g. rmrg")] string? sourceType = null) =>
        ExecuteAsync(async ct =>
        {
            var result = await Mediator.Send(new RetryKnowledgeSourcesCommand(key, sourceType), ct)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                return;
            }

            var report = result.Value;
            var message = report.Queued == 0
                ? $"None of the **{report.Matched}** matching source(s) was failed, skipped or unavailable — nothing to queue."
                : $"Queued **{report.Queued}** of **{report.Matched}** matching source(s). The coming runs pick them "
                  + "up; `/ai ingest` does it now.";

            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to queue the guide sources.");

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

        return $"Nothing was due for indexing. Sources: {AiSourcesFormatter.DescribeCounts(sources.Value)}.\n"
               + "Already-indexed sources are left alone until their re-index interval passes; "
               + "use `force: true` to re-index them now. Failed, skipped and unavailable ones are listed "
               + "by `/ai sources` and queued again by `/ai source-retry`.";
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

    private readonly IImageRelay? _imageRelay = serviceProvider.GetService<IImageRelay>();
}

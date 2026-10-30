using System.Text;
using Discord;
using Entities;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Ingestion;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

/// <summary>
/// Renders the guide-source registry for admins: the counts, a run's outcome, and the sources that need
/// a look.
///
/// Pure and static, like <see cref="AiFeedbackFormatter"/>, so the layout can be snapshot-tested without
/// a Discord connection.
/// </summary>
public static class AiSourcesFormatter
{
    /// <summary>
    /// Room the listing may take in an embed description. Discord allows 4096; the rest is head-room for
    /// the closing line, so a long reason can never push the reply over and fail the command.
    /// </summary>
    private const int DescriptionBudget = 3800;

    /// <summary>A reason is the extractor's own message and can run to the stored 512 characters.</summary>
    private const int ReasonPreviewLength = 160;

    private const int TitlePreviewLength = 60;

    /// <summary>The source counts as one line, for <c>/ai status</c> and an empty run's explanation.</summary>
    public static string DescribeCounts(KnowledgeSourceCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var line = $"{counts.Ingested} indexed · {counts.Pending} pending · {counts.Failed} failed · " +
                   $"{counts.Skipped} skipped · {counts.Unavailable} unavailable";

        return counts.Removed > 0 ? $"{line} · {counts.Removed} no longer listed" : line;
    }

    /// <summary>The outcome of an ingestion run, each kind of trouble named with what is done about it.</summary>
    public static string DescribeRun(IngestionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var message = new StringBuilder()
            .AppendLine($"Indexed **{report.Ingested}** source(s), **{report.ChunksWritten}** chunk(s).")
            .AppendLine($"Unchanged: {report.Unchanged} · Failed: {report.Failed} · Skipped: {report.Skipped}");

        if (report.Unavailable > 0)
        {
            message.AppendLine($"Unavailable: {report.Unavailable} ({report.Gone} deleted, {report.NotPublic} not shared) "
                               + "— checked again after `AI:Ingestion:UnavailableRecheckDays`.");
        }

        if (report.TooLarge > 0)
        {
            message.AppendLine($"Too large to export in any form: {report.TooLarge}.");
        }

        if (report.Removed > 0)
        {
            message.AppendLine($"Removed from the index: {report.Removed} (still deleted, or no longer listed).");
        }

        if (report.HostRefused > 0)
        {
            message.AppendLine($"\n⚠️ Left {report.HostRefused} source(s) for a later run: their site refused requests "
                               + "(rate limit or open circuit).");
        }

        if (report.BudgetExhausted)
        {
            message.AppendLine("\n⚠️ Stopped early — indexing has used its share of today's AI allowance.");
        }

        if (report.RateLimited)
        {
            message.AppendLine("\n⚠️ Stopped early — the AI provider kept rate-limiting even after waiting "
                               + "out its window. Something else may be using the same API key; the rest is "
                               + "picked up on the next run.");
        }

        if (report.Failed + report.Skipped + report.Unavailable > 0)
        {
            message.AppendLine("\nWhich ones, and why: `/ai sources`.");
        }

        return message.ToString();
    }

    /// <summary>
    /// The listed sources, as many as fit, each with its reason and its schedule. What did not fit is
    /// counted rather than dropped silently.
    /// </summary>
    public static Embed BuildListEmbed(KnowledgeSourceListing listing, KnowledgeSourceListKind kind, string? sourceType)
    {
        ArgumentNullException.ThrowIfNull(listing);

        var scope = string.IsNullOrWhiteSpace(sourceType) ? string.Empty : $" · {sourceType}";
        var builder = new EmbedBuilder().WithTitle($"📚 Guide sources — {Describe(kind)}{scope}");

        if (listing.Total == 0)
        {
            return builder.WithDescription(kind == KnowledgeSourceListKind.Problems
                    ? "Nothing is failing, skipped or unavailable."
                    : "No source is in that state.")
                .Build();
        }

        var description = new StringBuilder();
        var shown = 0;

        foreach (var source in listing.Sources)
        {
            var entry = Entry(source);
            if (description.Length + entry.Length > DescriptionBudget)
            {
                break;
            }

            description.Append(entry);
            shown++;
        }

        if (shown < listing.Total)
        {
            description.AppendLine($"Showing {shown} of {listing.Total}. Narrow it with `source-type`.");
        }

        if (kind != KnowledgeSourceListKind.Pending && kind != KnowledgeSourceListKind.Removed)
        {
            description.Append("Fixed upstream, or by an update? `/ai source-retry` queues them again.");
        }

        return builder.WithDescription(description.ToString()).Build();
    }

    private static string Entry(KnowledgeSourceSummary source)
    {
        var title = Shorten(source.Title ?? source.NaturalKey, TitlePreviewLength).Replace("]", ")", StringComparison.Ordinal);

        var entry = new StringBuilder()
            .AppendLine($"{Icon(source.Status)} **[{title}]({source.Url})** · `{source.SourceType}:{source.NaturalKey}`");

        if (!string.IsNullOrWhiteSpace(source.StatusReason))
        {
            entry.AppendLine($"> {Shorten(source.StatusReason.ReplaceLineEndings(" "), ReasonPreviewLength)}");
        }

        entry.AppendLine(Schedule(source)).AppendLine();
        return entry.ToString();
    }

    private static string Schedule(KnowledgeSourceSummary source)
    {
        var parts = new List<string>();

        if (source.ConsecutiveFailures > 0)
        {
            parts.Add(source.Status == KnowledgeSourceStatus.Unavailable
                ? $"unreadable on {source.ConsecutiveFailures} check(s) in a row"
                : $"{source.ConsecutiveFailures} failure(s) in a row");
        }

        parts.Add(source.LastAttemptedAtUtc is { } last ? $"last tried {Relative(last)}" : "never tried");

        if (source.RemovedFromSyncAtUtc is { } removed)
        {
            parts.Add($"unlisted {Relative(removed)}");
        }
        else if (source.IsDue)
        {
            parts.Add("due now");
        }
        else if (source.NextAttemptAtUtc is { } next)
        {
            parts.Add($"next {Relative(next)}");
        }
        else
        {
            parts.Add("not retried unless queued again");
        }

        return string.Join(" · ", parts);
    }

    private static string Describe(KnowledgeSourceListKind kind) => kind switch
    {
        KnowledgeSourceListKind.Problems => "needing a look",
        KnowledgeSourceListKind.Removed => "no longer listed",
        _ => kind.ToString().ToLowerInvariant()
    };

    private static string Icon(KnowledgeSourceStatus status) => status switch
    {
        KnowledgeSourceStatus.Failed => "⚠️",
        KnowledgeSourceStatus.Skipped => "⏭️",
        KnowledgeSourceStatus.Unavailable => "🚫",
        KnowledgeSourceStatus.Pending => "⏳",
        _ => "✅"
    };

    private static string Relative(DateTimeOffset at) =>
        TimestampTag.FromDateTimeOffset(at, TimestampTagStyles.Relative).ToString();

    private static string Shorten(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";
}

using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>Which sources an admin listing shows.</summary>
public enum KnowledgeSourceListKind
{
    /// <summary>Everything that is not indexed and not on its way to being: failed, skipped and unavailable.</summary>
    Problems,
    Failed,
    Skipped,
    Unavailable,
    Pending,

    /// <summary>Sources no catalogue lists any more, whatever their last status.</summary>
    Removed
}

/// <summary>
/// The sources that need an admin's eye, with why and when each is next tried.
///
/// The counts in <c>/ai status</c> said ten guides were failing, never which ones or why; the only other
/// trace was a redacted HTTP line in the logs. A deleted document, a private one and a site that changed
/// its markup each call for something different, and an admin cannot choose without the reason.
/// </summary>
/// <param name="SourceType">Restricts the listing to one extractor family.</param>
/// <param name="Limit">Sources returned; the total is reported regardless.</param>
public sealed record ListKnowledgeSourcesQuery(
    KnowledgeSourceListKind Kind = KnowledgeSourceListKind.Problems,
    string? SourceType = null,
    int Limit = 15) : IQuery<Result<KnowledgeSourceListing>>;

/// <param name="Total">How many sources matched, of which <paramref name="Sources"/> are the first.</param>
public sealed record KnowledgeSourceListing(IReadOnlyList<KnowledgeSourceSummary> Sources, int Total);

/// <param name="NextAttemptAtUtc">
/// When the queue next takes the source by itself. Null when it is due already (see
/// <paramref name="IsDue"/>), and when it never comes due by itself — skipped, or no longer listed.
/// </param>
/// <param name="IsDue">Whether the next run would take the source, room permitting.</param>
public sealed record KnowledgeSourceSummary(
    string SourceType,
    string NaturalKey,
    string Url,
    string? Title,
    KnowledgeSourceStatus Status,
    string? StatusReason,
    int ConsecutiveFailures,
    DateTimeOffset? LastAttemptedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    bool IsDue,
    DateTimeOffset? RemovedFromSyncAtUtc);

public sealed class ListKnowledgeSourcesHandler(
    IKnowledgeSourceRepository sources,
    IOptions<AiIngestionConfiguration> ingestionConfiguration)
    : IRequestHandler<ListKnowledgeSourcesQuery, Result<KnowledgeSourceListing>>
{
    /// <summary>Most sources returned at once; more would not fit one Discord embed anyway.</summary>
    private const int MaxLimit = 25;

    public async Task<Result<KnowledgeSourceListing>> Handle(
        ListKnowledgeSourcesQuery request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = IngestionSchedule.From(ingestionConfiguration.Value);

        var page = await sources.ListAsync(FilterFor(request), Math.Clamp(request.Limit, 1, MaxLimit), cancellationToken)
            .ConfigureAwait(false);

        return new KnowledgeSourceListing([.. page.Sources.Select(source => Summarise(source, schedule, now))], page.Total);
    }

    private static KnowledgeSourceListFilter FilterFor(ListKnowledgeSourcesQuery request)
    {
        var sourceType = string.IsNullOrWhiteSpace(request.SourceType) ? null : request.SourceType.Trim();

        return request.Kind switch
        {
            KnowledgeSourceListKind.Removed => new KnowledgeSourceListFilter([], RemovedFromSync: true, sourceType),
            KnowledgeSourceListKind.Failed => new KnowledgeSourceListFilter([KnowledgeSourceStatus.Failed], SourceType: sourceType),
            KnowledgeSourceListKind.Skipped => new KnowledgeSourceListFilter([KnowledgeSourceStatus.Skipped], SourceType: sourceType),
            KnowledgeSourceListKind.Unavailable => new KnowledgeSourceListFilter([KnowledgeSourceStatus.Unavailable], SourceType: sourceType),
            KnowledgeSourceListKind.Pending => new KnowledgeSourceListFilter([KnowledgeSourceStatus.Pending], SourceType: sourceType),
            _ => new KnowledgeSourceListFilter(
                [KnowledgeSourceStatus.Failed, KnowledgeSourceStatus.Skipped, KnowledgeSourceStatus.Unavailable],
                SourceType: sourceType)
        };
    }

    private static KnowledgeSourceSummary Summarise(KnowledgeSource source, KnowledgeSourceSchedule schedule, DateTimeOffset now)
    {
        var next = source.NextAttemptAtUtc(schedule);

        return new KnowledgeSourceSummary(
            source.SourceType,
            source.NaturalKey,
            source.Url,
            source.Title,
            source.Status,
            source.StatusReason,
            source.ConsecutiveFailures,
            source.LastAttemptedAtUtc,
            // A source never attempted is due "since the beginning of time"; that is no time to show.
            next is { } at && at > now ? at : null,
            next is { } due && due <= now,
            source.RemovedFromSyncAtUtc);
    }
}

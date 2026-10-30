namespace Entities;

public enum KnowledgeSourceStatus
{
    /// <summary>Known but not yet ingested.</summary>
    Pending = 0,
    Ingested,
    /// <summary>Fetching or parsing failed; retried with backoff.</summary>
    Failed,
    /// <summary>Deliberately not ingestible, e.g. a video or a link behind authentication.</summary>
    Skipped,

    /// <summary>
    /// Could not be read for a reason only its owner can undo: deleted, or no longer shared publicly.
    ///
    /// Neither a failure nor a skip. Retried with the failure backoff, a deleted document took a slot
    /// of every third run forever (nine of them, on production, every one answering 410); skipped, a
    /// document that is shared again never came back. So it is checked again rarely, on its own
    /// interval, and recovers on its own when the owner restores it.
    /// </summary>
    Unavailable
}

/// <summary>
/// When sources come due again. Passed in rather than held by the entity because both intervals are
/// operator settings.
/// </summary>
/// <param name="ReingestInterval">How long an indexed source is left alone before it is checked for changes.</param>
/// <param name="UnavailableRecheckInterval">How long an unavailable source is left alone before it is checked again.</param>
public readonly record struct KnowledgeSourceSchedule(TimeSpan ReingestInterval, TimeSpan UnavailableRecheckInterval)
{
    /// <summary>The recheck interval used when the caller names none.</summary>
    public static readonly TimeSpan DefaultUnavailableRecheckInterval = TimeSpan.FromDays(30);
}

public enum KnowledgeSourceOrigin
{
    /// <summary>Discovered by a library sync, and therefore removable by one.</summary>
    Sync = 0,
    /// <summary>Added by an admin; never removed automatically.</summary>
    Manual
}

/// <summary>
/// A document the bot indexes: a guide page, a shared document, an image album.
///
/// Kept in the relational store rather than only in the vector index so an operator can see what is
/// indexed, what failed and why, and what was deliberately skipped. Without it, a source that stops
/// parsing simply disappears from answers with nothing to point at.
/// </summary>
public class KnowledgeSource : BaseEntity
{
    /// <summary>Retry backoff doubles per consecutive failure up to this many doublings (~64 hours).</summary>
    private const int MaxBackoffDoublings = 6;

    /// <summary>
    /// Checks in a row that must find a source unreadable before its indexed content is dropped. One
    /// is not enough: a document that is briefly unshared, or a guide page that vanishes for an
    /// afternoon, would lose everything indexed from it. Two checks a recheck interval apart is.
    /// </summary>
    private const int UnavailableChecksBeforeRemoval = 2;

    /// <summary>
    /// Failure reasons come from exception messages and can be arbitrarily long, so they are clamped
    /// to the stored column's width. Held here rather than taken from the Constants project because
    /// the Domain layer deliberately depends on nothing but Utilities; the entity configuration
    /// declares the matching column length.
    /// </summary>
    private const int StatusReasonMaxLength = 512;

    public Guid SourceId { get; private set; }

    /// <summary>Extractor family, e.g. <c>plonkit</c>. Together with the key this identifies the source.</summary>
    public string SourceType { get; private set; } = string.Empty;

    /// <summary>Stable identity within the family — a country slug, a document id, an album id.</summary>
    public string NaturalKey { get; private set; } = string.Empty;

    public string Url { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public string? Country { get; private set; }

    public string? Continent { get; private set; }

    public string? Author { get; private set; }

    /// <summary>Editorial weight carried into retrieval ranking.</summary>
    public int Priority { get; private set; }

    public KnowledgeSourceOrigin Origin { get; private set; }

    /// <summary>Set false to drop a source from answers without losing its record.</summary>
    public bool Enabled { get; private set; } = true;

    public KnowledgeSourceStatus Status { get; private set; }

    /// <summary>Why the source is failed, skipped or unavailable, shown to admins.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>Hash of the last successfully ingested content, so unchanged sources are skipped.</summary>
    public string? ContentHash { get; private set; }

    /// <summary>Upstream's own change marker, when it publishes one — cheaper than hashing the body.</summary>
    public DateTimeOffset? SourceUpdatedAtUtc { get; private set; }

    public DateTimeOffset? LastAttemptedAtUtc { get; private set; }

    public DateTimeOffset? LastIngestedAtUtc { get; private set; }

    /// <summary>
    /// Attempts in a row that did not index the source. For an <see cref="KnowledgeSourceStatus.Unavailable"/>
    /// source it counts only the checks in a row that found it unreadable, starting again at one when
    /// it becomes unavailable, so it reads as "how many rechecks has this been gone for".
    /// </summary>
    public int ConsecutiveFailures { get; private set; }

    public int ChunkCount { get; private set; }

    public int ImageCount { get; private set; }

    /// <summary>
    /// True when a run indexed this source's text but not all of its images, for a reason that need not
    /// recur: the daily allowance ran out, the provider rate-limited, or it failed on its side. Such a
    /// source is usable but incomplete, so it is due again immediately rather than waiting out the normal
    /// re-ingest interval.
    ///
    /// Not set for an image the provider <em>rejects</em> — one it cannot fetch or read. Ingestion narrows
    /// a rejected batch down to that image and indexes the rest without it, because retrying a rejection
    /// fails the same way every run and spends the allowance each time.
    /// </summary>
    public bool ImagesDeferred { get; private set; }

    public DateTimeOffset FirstSeenAtUtc { get; private set; }

    /// <summary>
    /// Set when a sync stops listing this source. A tombstone rather than a delete, so a temporary
    /// edit upstream does not silently discard everything indexed from it.
    /// </summary>
    public DateTimeOffset? RemovedFromSyncAtUtc { get; private set; }

    public static KnowledgeSource Create(
        string sourceType,
        string naturalKey,
        string url,
        KnowledgeSourceOrigin origin,
        DateTimeOffset createdAtUtc,
        string? title = null,
        string? country = null,
        string? continent = null,
        string? author = null,
        int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return new KnowledgeSource
        {
            SourceId = Guid.NewGuid(),
            SourceType = sourceType,
            NaturalKey = naturalKey,
            Url = url,
            Origin = origin,
            Title = title,
            Country = country,
            Continent = continent,
            Author = author,
            Priority = priority,
            Status = KnowledgeSourceStatus.Pending,
            FirstSeenAtUtc = createdAtUtc
        };
    }

    /// <summary>Refreshes catalogue metadata from a sync without disturbing ingestion state.</summary>
    public void UpdateMetadata(string url, string? title, string? country, string? continent, string? author, int priority)
    {
        Url = url;
        Title = title;
        Country = country;
        Continent = continent;
        Author = author;
        Priority = priority;
        RemovedFromSyncAtUtc = null;
    }

    /// <param name="imagesDeferred">
    /// True when some images are still owed for a reason that may not recur, so the source comes back
    /// for them; see <see cref="ImagesDeferred"/>.
    /// </param>
    public void MarkIngested(
        string contentHash,
        DateTimeOffset? sourceUpdatedAtUtc,
        int chunkCount,
        int imageCount,
        DateTimeOffset nowUtc,
        bool imagesDeferred = false)
    {
        Status = KnowledgeSourceStatus.Ingested;
        StatusReason = null;
        ContentHash = contentHash;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
        ChunkCount = chunkCount;
        ImageCount = imageCount;
        ImagesDeferred = imagesDeferred;
        ConsecutiveFailures = 0;
        LastAttemptedAtUtc = nowUtc;
        LastIngestedAtUtc = nowUtc;
    }

    /// <summary>Whether unchanged content still needs work, because its images were never embedded.</summary>
    public bool NeedsImageBackfill => ImagesDeferred;

    /// <summary>
    /// Queues an indexed source to have its images embedded again, for pictures an earlier run lost.
    /// Only an indexed source can be missing them: a pending or failed one is re-indexed in full anyway,
    /// and a skipped one never will be.
    /// </summary>
    /// <returns>Whether the source was queued; false when it already was or is not indexed.</returns>
    public bool RequestImageBackfill()
    {
        if (Status != KnowledgeSourceStatus.Ingested || ImagesDeferred)
        {
            return false;
        }

        ImagesDeferred = true;
        return true;
    }

    public void MarkFailed(string reason, DateTimeOffset nowUtc)
    {
        Status = KnowledgeSourceStatus.Failed;
        StatusReason = Truncate(reason);
        ConsecutiveFailures++;
        LastAttemptedAtUtc = nowUtc;
    }

    /// <summary>Records a source that is understood but not ingestible, so coverage reporting stays honest.</summary>
    public void MarkSkipped(string reason, DateTimeOffset nowUtc)
    {
        Status = KnowledgeSourceStatus.Skipped;
        StatusReason = Truncate(reason);
        LastAttemptedAtUtc = nowUtc;
    }

    /// <summary>
    /// Records a source its owner has deleted or stopped sharing. It is checked again after the
    /// schedule's recheck interval rather than retried with backoff, and comes back by itself once it
    /// can be read again.
    /// </summary>
    public void MarkUnavailable(string reason, DateTimeOffset nowUtc)
    {
        // Counted afresh on entry, so the count means checks in a row that found it unreadable — which
        // is what deciding to drop its indexed content needs, not failures of any kind before it.
        ConsecutiveFailures = Status == KnowledgeSourceStatus.Unavailable ? ConsecutiveFailures + 1 : 1;
        Status = KnowledgeSourceStatus.Unavailable;
        StatusReason = Truncate(reason);
        LastAttemptedAtUtc = nowUtc;
    }

    /// <summary>
    /// Whether this source has been found unreadable often enough in a row that what is indexed from it
    /// should go: an answer citing it would send the reader to a dead link.
    /// </summary>
    public bool IsConfirmedUnavailable =>
        Status == KnowledgeSourceStatus.Unavailable && ConsecutiveFailures >= UnavailableChecksBeforeRemoval;

    /// <summary>Whether the vector index may still hold chunks of this source.</summary>
    public bool HoldsIndexedContent => ContentHash is not null || ChunkCount > 0 || ImageCount > 0;

    /// <summary>
    /// Whether the index holds pictures of this source: embedded ones, or ones still owed their embedding.
    /// The second kind counts too, because their chunks are written — only the image vectors are missing —
    /// and a source whose every picture was postponed has an <see cref="ImageCount"/> of zero.
    /// </summary>
    public bool HoldsIndexedPictures => ImageCount > 0 || ImagesDeferred;

    /// <summary>
    /// Records that this source's chunks were removed from the index. The content hash goes with them,
    /// so a source that comes back is written in full rather than judged unchanged against chunks that
    /// no longer exist.
    /// </summary>
    public void MarkIndexedContentRemoved()
    {
        ContentHash = null;
        ChunkCount = 0;
        ImageCount = 0;
        ImagesDeferred = false;
    }

    /// <summary>
    /// Whether the last attempt went wrong. Such sources are retried last in a run and only a few at a
    /// time, so a handful of dead links cannot crowd out work that will succeed.
    /// </summary>
    public bool IsRetrying => ConsecutiveFailures > 0;

    /// <summary>
    /// Puts a failed, unavailable or skipped source back in the queue at once, for an operator who knows
    /// something has changed — an extractor fixed after a site redesign, a document shared again.
    ///
    /// Without it nothing could bring a skipped source back: skipping is meant to be permanent, and a
    /// guide site that changed its markup had every guide skipped as "not a guide page".
    /// </summary>
    /// <returns>Whether the source was queued; false when it was not in one of those states.</returns>
    public bool RequestRetry()
    {
        if (Status is not (KnowledgeSourceStatus.Failed or KnowledgeSourceStatus.Unavailable or KnowledgeSourceStatus.Skipped))
        {
            return false;
        }

        // The previous verdict stays readable until the retry replaces it, so an admin listing the queue
        // can still see why it was there.
        StatusReason = Truncate($"Queued again by an admin; was {Status}: {StatusReason}");
        Status = KnowledgeSourceStatus.Pending;
        ConsecutiveFailures = 0;
        return true;
    }

    /// <summary>Records an attempt that found nothing new, so re-ingest cadence still advances.</summary>
    public void MarkUnchanged(DateTimeOffset nowUtc)
    {
        Status = KnowledgeSourceStatus.Ingested;
        StatusReason = null;
        ConsecutiveFailures = 0;
        LastAttemptedAtUtc = nowUtc;
    }

    public void MarkRemovedFromSync(DateTimeOffset nowUtc) => RemovedFromSyncAtUtc = nowUtc;

    public void SetEnabled(bool enabled) => Enabled = enabled;

    /// <summary>
    /// Whether this source is worth attempting now. Failures back off exponentially so a permanently
    /// broken link cannot consume a run's whole budget every night.
    /// </summary>
    public bool IsDueForIngest(DateTimeOffset nowUtc, TimeSpan reingestInterval) =>
        IsDueForIngest(nowUtc,
            new KnowledgeSourceSchedule(reingestInterval, KnowledgeSourceSchedule.DefaultUnavailableRecheckInterval));

    /// <inheritdoc cref="IsDueForIngest(DateTimeOffset, TimeSpan)"/>
    public bool IsDueForIngest(DateTimeOffset nowUtc, KnowledgeSourceSchedule schedule) =>
        NextAttemptAtUtc(schedule) is { } next && next <= nowUtc;

    /// <summary>
    /// When this source next comes due, or null when it never does by itself — skipped, disabled, or no
    /// longer listed by any catalogue. A time in the past means it is due now.
    ///
    /// The one place the schedule is decided: the due test is derived from it, so what an admin is
    /// shown as the next attempt cannot drift from what the queue actually does.
    /// </summary>
    public DateTimeOffset? NextAttemptAtUtc(KnowledgeSourceSchedule schedule)
    {
        if (!Enabled || Status == KnowledgeSourceStatus.Skipped || RemovedFromSyncAtUtc is not null)
        {
            return null;
        }

        // Never attempted, or put back in the queue by an admin.
        if (Status == KnowledgeSourceStatus.Pending || LastAttemptedAtUtc is not { } lastAttempt)
        {
            return DateTimeOffset.MinValue;
        }

        if (Status == KnowledgeSourceStatus.Unavailable)
        {
            return lastAttempt + schedule.UnavailableRecheckInterval;
        }

        if (ConsecutiveFailures > 0)
        {
            return lastAttempt + TimeSpan.FromHours(Math.Pow(2, Math.Min(ConsecutiveFailures, MaxBackoffDoublings)));
        }

        // Postponed images make the source incomplete, so it goes back in the queue at once instead
        // of waiting weeks for its pictures.
        return ImagesDeferred ? lastAttempt : lastAttempt + schedule.ReingestInterval;
    }

    private static string Truncate(string reason) =>
        reason.Length <= StatusReasonMaxLength ? reason : reason[..StatusReasonMaxLength];

    private KnowledgeSource()
    {
    }

    public override string ToString() => $"{SourceType}:{NaturalKey} ({Status})";
}

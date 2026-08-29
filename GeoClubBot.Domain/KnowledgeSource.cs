namespace Entities;

public enum KnowledgeSourceStatus
{
    /// <summary>Known but not yet ingested.</summary>
    Pending = 0,
    Ingested,
    /// <summary>Fetching or parsing failed; retried with backoff.</summary>
    Failed,
    /// <summary>Deliberately not ingestible, e.g. a video or a link behind authentication.</summary>
    Skipped
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

    /// <summary>Why the source is failed or skipped, shown to admins.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>Hash of the last successfully ingested content, so unchanged sources are skipped.</summary>
    public string? ContentHash { get; private set; }

    /// <summary>Upstream's own change marker, when it publishes one — cheaper than hashing the body.</summary>
    public DateTimeOffset? SourceUpdatedAtUtc { get; private set; }

    public DateTimeOffset? LastAttemptedAtUtc { get; private set; }

    public DateTimeOffset? LastIngestedAtUtc { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public int ChunkCount { get; private set; }

    public int ImageCount { get; private set; }

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

    public void MarkIngested(string contentHash, DateTimeOffset? sourceUpdatedAtUtc, int chunkCount, int imageCount, DateTimeOffset nowUtc)
    {
        Status = KnowledgeSourceStatus.Ingested;
        StatusReason = null;
        ContentHash = contentHash;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
        ChunkCount = chunkCount;
        ImageCount = imageCount;
        ConsecutiveFailures = 0;
        LastAttemptedAtUtc = nowUtc;
        LastIngestedAtUtc = nowUtc;
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
    public bool IsDueForIngest(DateTimeOffset nowUtc, TimeSpan reingestInterval)
    {
        if (!Enabled || Status == KnowledgeSourceStatus.Skipped || RemovedFromSyncAtUtc is not null)
        {
            return false;
        }

        if (LastAttemptedAtUtc is not { } lastAttempt)
        {
            return true;
        }

        if (ConsecutiveFailures > 0)
        {
            var backoff = TimeSpan.FromHours(Math.Pow(2, Math.Min(ConsecutiveFailures, MaxBackoffDoublings)));
            return nowUtc - lastAttempt >= backoff;
        }

        return nowUtc - lastAttempt >= reingestInterval;
    }

    private static string Truncate(string reason) =>
        reason.Length <= StatusReasonMaxLength ? reason : reason[..StatusReasonMaxLength];

    private KnowledgeSource()
    {
    }

    public override string ToString() => $"{SourceType}:{NaturalKey} ({Status})";
}

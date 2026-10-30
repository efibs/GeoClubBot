namespace Configuration;

public class AiIngestionConfiguration
{
    public const string SectionName = "AI:Ingestion";

    /// <summary>
    /// Sources processed per run. Bounded because each one costs embedding requests against the
    /// provider's daily allowance, and because hammering a third-party site is rude.
    /// </summary>
    public int MaxSourcesPerRun { get; set; } = 25;

    /// <summary>How long an already-ingested source is left alone before it is checked again.</summary>
    public int ReingestAfterDays { get; set; } = 14;

    /// <summary>
    /// Largest document export downloaded, in bytes.
    ///
    /// A Google Docs export carries the document's images, and a guide of a few pages can carry a
    /// third of a gigabyte of them — measured, on a library document. Nothing announces that size
    /// beforehand, so it is read until it is over this and then abandoned; the text export is taken
    /// instead, which is a few kilobytes and most of what the model reads anyway.
    /// </summary>
    public int MaxDocumentExportBytes { get; set; } = 32 * 1024 * 1024;

    /// <summary>
    /// Largest Google Slides deck downloaded, in bytes. A deck past it is indexed from its text export,
    /// without pictures, speaker notes or slide links.
    ///
    /// Its own limit because decks are far heavier than documents and their pictures are the point:
    /// measured across the library's 41 indexed decks, the median export is 19 MB, 17 are over the 32 MB
    /// document limit and the largest is 339 MB. Held to the document limit, those 17 would have lost
    /// every indexed picture on their next re-index. A deck is held in memory while it is read, because
    /// the PowerPoint reader needs random access.
    /// </summary>
    public int MaxDeckExportBytes { get; set; } = 512 * 1024 * 1024;

    /// <summary>
    /// Days before a source that was deleted or stopped being shared is checked again.
    ///
    /// Retried with the failure backoff instead, nine deleted documents took a slot of every third run on
    /// production, for ever; skipped, a document shared again would never come back. A check a month is
    /// one cheap request, and the second check that still finds a source deleted removes it from the index.
    /// </summary>
    public int UnavailableRecheckDays { get; set; } = 30;

    /// <summary>
    /// Most sources whose last attempt failed that one run retries. They are taken last, fewest failures
    /// first, so sources that will probably work are never crowded out by a backlog that will probably not.
    /// </summary>
    public int MaxFailingRetriesPerRun { get; set; } = 5;

    /// <summary>
    /// Days a source may go unlisted by every catalogue before its chunks are removed from the index; 0
    /// keeps them for ever. A grace period rather than at once, because a library edit can drop a guide
    /// for a week and put it back, and the catalogue sync that would notice runs only weekly.
    /// </summary>
    public int RemovedSourceGraceDays { get; set; } = 30;

    /// <summary>
    /// Longest one fetch of third-party content may take, per attempt. A Google Docs export is by far
    /// the slowest thing the job asks for: Google spends up to a minute building one before it either
    /// arrives or is refused, and at 30 seconds every one of those was abandoned mid-build.
    /// </summary>
    public int SourceRequestTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Longest a fetch may take in total: queueing behind the politeness limiter, every attempt, and
    /// the backoff between them.
    ///
    /// Enforced by the HttpClient, so it has to exceed the attempts it is meant to contain. At 30
    /// seconds it was shorter than a single slow export, and a document that would have arrived was
    /// reported as unreachable — and retried the same way on every run.
    /// </summary>
    public int SourceOverallTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Share of the daily AI request allowance that indexing may consume, as a percentage.
    ///
    /// Indexing and answering draw on the same daily counter, and the indexing job runs overnight —
    /// so without a ceiling a backfill would spend the whole allowance before anyone is awake and the
    /// bot would be mute all day. Reserving the remainder keeps questions working while a large
    /// library is indexed over several nights.
    /// </summary>
    public int MaxDailyBudgetPercent { get; set; } = 60;

    /// <summary>
    /// Google Sheets id of the community guide library to sync from. Empty disables the sync, which
    /// is the default: the library belongs to someone else, so pointing at it is an operator's choice.
    /// </summary>
    public string? MetaLibrarySheetId { get; set; }

    /// <summary>
    /// Whether to embed images at all. Some sites block unattended fetches of their images, and the
    /// embedding provider fetches them server-side, so image embedding can be turned off per
    /// deployment without losing the text.
    /// </summary>
    public bool EmbedImages { get; set; } = true;
}

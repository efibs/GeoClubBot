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

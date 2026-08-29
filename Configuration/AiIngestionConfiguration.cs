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
    /// Share of the daily AI request allowance that indexing may consume, as a percentage.
    ///
    /// Indexing and answering draw on the same daily counter, and the indexing job runs overnight —
    /// so without a ceiling a backfill would spend the whole allowance before anyone is awake and the
    /// bot would be mute all day. Reserving the remainder keeps questions working while a large
    /// library is indexed over several nights.
    /// </summary>
    public int MaxDailyBudgetPercent { get; set; } = 60;

    /// <summary>
    /// Whether to embed images at all. Some sites block unattended fetches of their images, and the
    /// embedding provider fetches them server-side, so image embedding can be turned off per
    /// deployment without losing the text.
    /// </summary>
    public bool EmbedImages { get; set; } = true;
}

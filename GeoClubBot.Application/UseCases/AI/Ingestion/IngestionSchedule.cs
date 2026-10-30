using Configuration;
using Entities;

namespace UseCases.UseCases.AI.Ingestion;

/// <summary>
/// The queue's schedule as configured. Read in one place so the run that decides what is due and the
/// listing that tells an admin when a source is next tried cannot disagree about it.
/// </summary>
internal static class IngestionSchedule
{
    /// <summary>
    /// At least a day each: an interval of zero would make every source due on every run, which for an
    /// unavailable one means asking a host about a deleted document every night.
    /// </summary>
    public static KnowledgeSourceSchedule From(AiIngestionConfiguration settings) =>
        new(TimeSpan.FromDays(Math.Max(1, settings.ReingestAfterDays)),
            TimeSpan.FromDays(Math.Max(1, settings.UnavailableRecheckDays)));
}

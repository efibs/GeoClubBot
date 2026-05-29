using System.Diagnostics.Metrics;

namespace UseCases.Observability;

/// <summary>
/// Quartz job execution metrics. The listener (in Infrastructure) records each job's
/// runtime + failure outcome here so dashboards can compare schedules against drift.
/// </summary>
public static class JobMetrics
{
    public static readonly Histogram<double> JobDurationMs = HandlerMetrics.Meter.CreateHistogram<double>(
        name: "geoclubbot.job.duration",
        unit: "ms",
        description: "Duration of a Quartz job execution in milliseconds.");

    public static readonly Counter<long> JobFailures = HandlerMetrics.Meter.CreateCounter<long>(
        name: "geoclubbot.job.failures",
        unit: "{failure}",
        description: "Count of Quartz job executions that surfaced a JobExecutionException.");
}

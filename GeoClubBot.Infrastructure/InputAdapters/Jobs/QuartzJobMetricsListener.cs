using Quartz;
using UseCases.Observability;

namespace Infrastructure.InputAdapters.Jobs;

/// <summary>
/// Records every Quartz job's runtime + failure outcome into <see cref="JobMetrics"/>.
/// Wired in <c>QuartzModule</c> via the scheduler's job-listener registration.
/// </summary>
public sealed class QuartzJobMetricsListener : IJobListener
{
    public string Name => nameof(QuartzJobMetricsListener);

    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        var jobName = context.JobDetail.JobType.Type.Name;
        var jobTag = new KeyValuePair<string, object?>("job_name", jobName);

        JobMetrics.JobDurationMs.Record(context.JobRunTime.TotalMilliseconds, jobTag);

        if (jobException is not null)
        {
            JobMetrics.JobFailures.Add(1, jobTag);
        }

        return ValueTask.CompletedTask;
    }
}

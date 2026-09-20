using Constants;
using Infrastructure.InputAdapters.Jobs;
using Quartz;
using QuartzExtensions;

namespace GeoClubBot.DependencyInjection.Modules;

public static class QuartzModule
{
    public static IServiceCollection AddQuartzModule(this IServiceCollection services)
    {
        services.AddSingleton<QuartzJobMetricsListener>();

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(o => o.InstanceId = StringConstants.QuartzSchedulerName);

            var commandsAssembly = typeof(IJobAssemblyMarker).Assembly;
            q.AddCronJobs(commandsAssembly);

            // Listener records per-job duration + failures into JobMetrics.
            q.AddJobListener<QuartzJobMetricsListener>([GroupMatcher<JobKey>.AnyGroup()]);
        });

        services.AddQuartzHostedService(options =>
        {
            options.AwaitApplicationStarted = true;
            // when shutting down we want jobs to complete gracefully
            options.WaitForJobsToComplete = true;
        });

        return services;
    }
}

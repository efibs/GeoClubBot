using Constants;
using Infrastructure.InputAdapters.Jobs;
using Quartz;
using QuartzExtensions;

namespace GeoClubBot.DependencyInjection.Modules;

public static class QuartzModule
{
    public static IServiceCollection AddQuartzModule(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            q.SchedulerId = StringConstants.QuartzSchedulerName;

            var commandsAssembly = typeof(JobAssemblyMarker).Assembly;
            q.AddCronJobs(commandsAssembly);
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

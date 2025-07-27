using System.Reflection;
using Quartz;

namespace QuartzExtensions;

public static class QuartzDependencyInjectionExtensions
{
    public static void AddCronJobs(this IServiceCollectionQuartzConfigurator q, Assembly assembly)
    {
        // Get the job type 
        var jobType = typeof(IJob);
        
        // Get all the cron job types
        var cronJobTypes = assembly.GetTypes()
            .Where(t => jobType.IsAssignableFrom(t))
            .ToList();
        
        // For every cron job
        foreach (var cronJobType in cronJobTypes)
        {
            // Get the cron job attributes
            var cronJobAttribute = cronJobType.GetCustomAttribute<CronJobAttribute>();
            
            // If the type has not set a cron job attribute
            if (cronJobAttribute == null)
            {
                continue;
            }
            
            // Create the job key
            var jobKey = new JobKey(cronJobType.Name);
            
            // Add the job
            q.AddJob(cronJobType, jobKey);

            // Add the trigger
            q.AddTrigger(o => o.ForJob(jobKey)
                .WithIdentity(cronJobType.Name + "-trigger")
                .WithCronSchedule(cronJobAttribute.CronSchedule, b => b.InTimeZone(TimeZoneInfo.Utc)));
        }
    }
}
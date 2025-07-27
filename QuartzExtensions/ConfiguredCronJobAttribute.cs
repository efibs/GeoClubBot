using Microsoft.Extensions.Configuration;

namespace QuartzExtensions;

public class ConfiguredCronJobAttribute(string configurationKey) : CronJobAttribute(_getCronSchedule(configurationKey))
{
    private static string _getCronSchedule(string configurationKey)
    {
        // If the config is not set yet
        if (Config == null)
        {
            throw new InvalidOperationException("Configuration is not set yet.");
        }
        
        // Get the cron schedule from the config
        var cronSchedule = Config.GetValue<string>(configurationKey);
        
        // If the cron schedule is not set
        if (cronSchedule == null)
        {
            throw new InvalidOperationException($"CronSchedule of configuration '{configurationKey}' is not set.");
        }
        
        return cronSchedule;
    }

    public static IConfiguration? Config = null;
}
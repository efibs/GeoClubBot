using Microsoft.Extensions.Configuration;

namespace QuartzExtensions;

/// <param name="configurationKey">Key of the cron expression.</param>
/// <param name="timeZoneConfigurationKey">
/// Optional key of an IANA time zone id the expression is evaluated in. Absent or empty means UTC, so
/// jobs that don't pass one behave exactly as before.
/// </param>
public class ConfiguredCronJobAttribute(string configurationKey, string? timeZoneConfigurationKey = null)
    : CronJobAttribute(GetCronSchedule(configurationKey))
{
    public override TimeZoneInfo TimeZone { get; } = GetTimeZone(timeZoneConfigurationKey);

    private static string GetCronSchedule(string configurationKey)
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

    private static TimeZoneInfo GetTimeZone(string? timeZoneConfigurationKey)
    {
        if (timeZoneConfigurationKey == null)
        {
            return TimeZoneInfo.Utc;
        }

        if (Config == null)
        {
            throw new InvalidOperationException("Configuration is not set yet.");
        }

        var timeZoneId = Config.GetValue<string>(timeZoneConfigurationKey);
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
        {
            throw new InvalidOperationException(
                $"Time zone '{timeZoneId}' of configuration '{timeZoneConfigurationKey}' is not a known time zone. " +
                "Use an IANA id such as 'Europe/Berlin'.");
        }

        return timeZone;
    }

    public static IConfiguration? Config = null;
}

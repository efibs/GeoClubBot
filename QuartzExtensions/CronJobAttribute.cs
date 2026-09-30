namespace QuartzExtensions;

[AttributeUsage(AttributeTargets.Class)]
public class CronJobAttribute(string cronSchedule) : Attribute
{
    public virtual string CronSchedule => cronSchedule;

    /// <summary>The time zone the cron expression is evaluated in. UTC unless a job asks for another.</summary>
    public virtual TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
}

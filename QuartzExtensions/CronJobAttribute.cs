namespace QuartzExtensions;

[AttributeUsage(AttributeTargets.Class)]
public class CronJobAttribute(string cronSchedule) : Attribute
{
    public virtual string CronSchedule => cronSchedule;
}
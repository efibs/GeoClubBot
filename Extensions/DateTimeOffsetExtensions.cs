namespace Extensions;

public static class DateTimeOffsetExtensions
{
    public static DateTimeOffset Truncate(this DateTimeOffset dateTimeOffset, TimeSpan interval)
    {
        // Sanity check
        if (interval == TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), $"{nameof(interval)} cannot be zero.");
        }

        // Convert to ticks and truncate as ticks
        var newTicks = dateTimeOffset.Ticks - (dateTimeOffset.Ticks % interval.Ticks);

        return new DateTimeOffset(newTicks, TimeSpan.Zero);
    }

    public static DateTimeOffset RoundUp(this DateTimeOffset dateTimeOffset, TimeSpan interval)
    {
        // First truncate, then add the interval
        return dateTimeOffset.Truncate(interval).Add(interval);
    }
}
using System.Globalization;

namespace Extensions;

public static class TimeSpanExtensions
{
    /// <summary>
    /// The two largest units of a duration, for people to read: <c>2d 3h</c>, <c>4h 12m</c>,
    /// <c>35m</c>, or <c>&lt;1m</c>. Negative durations are shown as zero.
    /// </summary>
    public static string ToShortDuration(this TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1))
        {
            return "<1m";
        }

        var days = (int)duration.TotalDays;
        if (days > 0)
        {
            return duration.Hours > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{days}d {duration.Hours}h")
                : string.Create(CultureInfo.InvariantCulture, $"{days}d");
        }

        if (duration.Hours > 0)
        {
            return duration.Minutes > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{duration.Hours}h {duration.Minutes}m")
                : string.Create(CultureInfo.InvariantCulture, $"{duration.Hours}h");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes}m");
    }

    /// <summary>
    /// A configured span for a sentence such as "last 14 days": whole days are spelled out, anything
    /// else falls back to <see cref="ToShortDuration"/>.
    /// </summary>
    public static string ToReadableSpan(this TimeSpan span)
    {
        if (span > TimeSpan.Zero && span.Ticks % TimeSpan.TicksPerDay == 0)
        {
            var days = (int)span.TotalDays;
            return days == 1 ? "day" : string.Create(CultureInfo.InvariantCulture, $"{days} days");
        }

        return span.ToShortDuration();
    }
}

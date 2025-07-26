namespace Utilities;

public record struct TimeRange(DateTimeOffset From, DateTimeOffset To)
{
    public TimeRange() : this(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
    {
        
    }
    
    public bool Intersects(TimeRange other)
    {
        return From <= other.To && To >= other.From;
    }

    public bool Contains(DateTimeOffset time)
    {
        return time >= From && time <= To;
    }

    public TimeSpan ToTimeSpan()
    {
        return To - From;
    }
    
    public static TimeRange operator +(TimeRange x, TimeRange y)
    {
        // If the time ranges do not intersect
        if (!x.Intersects(y))
        {
            throw new ArgumentException("Time ranges must intersect");
        }
        
        // Calculate the new time range
        var newFrom = x.From < y.From ? x.From : y.From;
        var newTo = x.To > y.To ? x.To : y.To;
        
        return new TimeRange(newFrom, newTo);
    }

    public static TimeRange operator &(TimeRange x, TimeRange y)
    {
        // If the time ranges do not intersect
        if (!x.Intersects(y))
        {
            throw new ArgumentException("Time ranges must intersect");
        }
        
        // Calculate the new time range
        var newFrom = x.From > y.From ? x.From : y.From;
        var newTo = x.To < y.To ? x.To : y.To;
        
        return new TimeRange(newFrom, newTo);
    }
    
    public double CalculateFreePercent(List<TimeRange> blockingTimeRanges)
    {
        // Squash the blocking time ranges
        var squashedBlockedTimeRanges = Squash(blockingTimeRanges);
        
        // Calculate this time span
        var thisTimeSpan = ToTimeSpan();
        
        // Calculate the sum of the blocked time spans
        var blockedTimeSpan = squashedBlockedTimeRanges
            .Select(r => r.ToTimeSpan())
            .Aggregate(TimeSpan.Zero, (a, b) => a + b);
        
        // Calculate the blocked percentage
        var blockedPercentage = blockedTimeSpan / thisTimeSpan;
        
        // Subtract from one to get the free percentage
        return 1.0 - blockedPercentage;
    }

    public static List<TimeRange> Squash(List<TimeRange> timeRanges)
    {
        var result = new Stack<TimeRange>();
        
        // Order the time ranges by from timestamp
        var orderedTimeRanges = timeRanges.OrderBy(r => r.From);
        
        // For every time range
        foreach (var timeRange in orderedTimeRanges)
        {
            // If the result is still empty
            if (result.Count == 0)
            {
                // Simply add the time range
                result.Push(timeRange);
                continue;
            }
            
            // Get the last time range
            var latestTimeRange = result.Peek();
            
            // If the time ranges intersect
            if (latestTimeRange.Intersects(timeRange))
            {
                // Pop the last result
                result.Pop();
                
                // Calculate the union
                var union = latestTimeRange + timeRange;
                
                // Push to stack
                result.Push(union);
            }
            else
            {
                // Simply push the new time range
                result.Push(timeRange);
            }
        }
        
        return result.ToList();
    }
}
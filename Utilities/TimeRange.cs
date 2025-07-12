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
}
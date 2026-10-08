namespace GeoClubBot.Tests.TestBuilders;

/// <summary>
/// A clock that stands still until a test moves it. TimeProvider is abstract with a virtual
/// GetUtcNow, so this needs no extra test package.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

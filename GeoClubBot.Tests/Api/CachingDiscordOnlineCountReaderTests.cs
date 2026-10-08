using Configuration;
using FluentAssertions;
using GeoClubBot.DependencyInjection;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using Xunit;

namespace GeoClubBot.Tests.Api;

public sealed class CachingDiscordOnlineCountReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 12, 40, TimeSpan.Zero);

    private readonly IDiscordServerStatsAccess _serverStats = Substitute.For<IDiscordServerStatsAccess>();
    private readonly ILogger<CachingDiscordOnlineCountReader> _logger = Substitute.For<ILogger<CachingDiscordOnlineCountReader>>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FixedTimeProvider _clock = new(Now);

    public CachingDiscordOnlineCountReaderTests()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
    }

    private CachingDiscordOnlineCountReader CreateReader() => new(
        _serverStats, _cache,
        Options.Create(new DiscordConfiguration
        {
            BotToken = "x",
            ServerId = 1,
            WelcomeMessage = "x",
            WelcomeTextChannelId = 1,
            LeftMessage = "x",
            LeftTextChannelId = 1
        }),
        _clock,
        _logger);

    [Fact]
    public async Task ReadOnlineCount_ReturnsTheCount_WithTheTimeItWasRead()
    {
        _serverStats.ReadApproximateOnlineCountAsync(Arg.Any<CancellationToken>()).Returns(18);

        var count = await CreateReader().ReadOnlineCountAsync();

        count.Should().Be(new DiscordOnlineCount(18, Now));
    }

    [Fact]
    public async Task ReadOnlineCount_CachesTheCount()
    {
        _serverStats.ReadApproximateOnlineCountAsync(Arg.Any<CancellationToken>()).Returns(18, 19);

        var first = await CreateReader().ReadOnlineCountAsync();
        _clock.Now = Now.AddSeconds(30);
        var second = await CreateReader().ReadOnlineCountAsync();

        second.Should().Be(first);
        await _serverStats.Received(1).ReadApproximateOnlineCountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadOnlineCount_ReturnsNullAndWarns_WhenDiscordFails()
    {
        _serverStats.ReadApproximateOnlineCountAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("no count"));

        var count = await CreateReader().ReadOnlineCountAsync();

        count.Should().BeNull();
        _logger.Received().Log(LogLevel.Warning, Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception?>(), Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ReadOnlineCount_DoesNotCacheAFailure()
    {
        _serverStats.ReadApproximateOnlineCountAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException("no count"),
                _ => 18);

        var first = await CreateReader().ReadOnlineCountAsync();
        var second = await CreateReader().ReadOnlineCountAsync();

        first.Should().BeNull();
        second.Should().Be(new DiscordOnlineCount(18, Now));
    }

    [Fact]
    public async Task ReadOnlineCount_ReturnsNull_WhenTheRequestTimesOut()
    {
        _serverStats.ReadApproximateOnlineCountAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("timeout"));

        var count = await CreateReader().ReadOnlineCountAsync();

        count.Should().BeNull();
    }

    [Fact]
    public async Task ReadOnlineCount_PassesOnTheCallersCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _serverStats.ReadApproximateOnlineCountAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        var read = () => CreateReader().ReadOnlineCountAsync(cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }
}

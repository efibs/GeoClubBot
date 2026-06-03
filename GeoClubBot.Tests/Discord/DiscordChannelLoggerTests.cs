using Configuration;
using FluentAssertions;
using GeoClubBot.Discord.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// The logger is the hot path that runs on every caller's thread, so its filtering is what keeps
/// the sink cheap and loop-free: only the configured level and above, never the Discord.Net /
/// HttpClient categories that delivery itself produces.
/// </summary>
public sealed class DiscordChannelLoggerTests
{
    private readonly DiscordChannelLogQueue _queue = new();

    private ILogger CreateLogger(string category, DiscordLoggingConfiguration config)
    {
        var provider = new DiscordChannelLoggerProvider(_queue, Options.Create(config));
        return provider.CreateLogger(category);
    }

    private static DiscordLoggingConfiguration Enabled(LogLevel min = LogLevel.Warning) =>
        new() { ChannelId = 123, MinimumLogLevel = min };

    private bool TryDequeue(out DiscordLogEntry entry) => _queue.Reader.TryRead(out entry);

    [Fact]
    public void Log_BelowMinimumLevel_IsNotEnqueued()
    {
        var logger = CreateLogger("MyApp.Service", Enabled(LogLevel.Warning));

        logger.LogInformation("just info");

        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        TryDequeue(out _).Should().BeFalse();
    }

    [Fact]
    public void Log_AtMinimumLevel_IsEnqueuedWithMessage()
    {
        var logger = CreateLogger("MyApp.Service", Enabled(LogLevel.Warning));

        logger.LogWarning("something looks off");

        TryDequeue(out var entry).Should().BeTrue();
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Category.Should().Be("MyApp.Service");
        entry.Message.Should().Be("something looks off");
        entry.ExceptionLine.Should().BeNull();
    }

    [Fact]
    public void Log_WithException_CapturesSingleLineSummaryWithoutStackTrace()
    {
        var logger = CreateLogger("MyApp.Service", Enabled(LogLevel.Warning));

        logger.LogError(new InvalidOperationException("boom"), "operation failed");

        TryDequeue(out var entry).Should().BeTrue();
        entry.Message.Should().Be("operation failed");
        entry.ExceptionLine.Should().Be("InvalidOperationException: boom");
        entry.ExceptionLine.Should().NotContain("\n");
    }

    [Theory]
    [InlineData("Discord")]
    [InlineData("Discord.WebSocket")]
    [InlineData("Discord.Rest")]
    [InlineData("System.Net.Http.HttpClient")]
    [InlineData("GeoClubBot.Discord.Logging.DiscordChannelLogProcessor")]
    public void Log_FromExcludedCategory_IsNotEnqueued(string category)
    {
        var logger = CreateLogger(category, Enabled(LogLevel.Warning));

        logger.LogError("this would otherwise loop");

        logger.IsEnabled(LogLevel.Error).Should().BeFalse();
        TryDequeue(out _).Should().BeFalse();
    }

    [Fact]
    public void Log_WhenSinkDisabled_IsNotEnqueued()
    {
        // ChannelId 0 => disabled, even for errors.
        var logger = CreateLogger("MyApp.Service", new DiscordLoggingConfiguration { ChannelId = 0 });

        logger.LogError("boom");

        logger.IsEnabled(LogLevel.Error).Should().BeFalse();
        TryDequeue(out _).Should().BeFalse();
    }
}

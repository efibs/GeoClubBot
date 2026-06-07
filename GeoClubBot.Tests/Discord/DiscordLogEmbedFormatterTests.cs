using Discord;
using FluentAssertions;
using GeoClubBot.Discord.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoClubBot.Tests.Discord;

public sealed class DiscordLogEmbedFormatterTests
{
    private readonly DiscordLogEmbedFormatter _formatter = new();

    private static readonly DateTimeOffset Timestamp =
        new(2026, 6, 3, 14, 22, 1, TimeSpan.Zero);

    [Fact]
    public void Build_WarningWithException_RendersShortTitleColorAndExceptionLine()
    {
        var entry = new DiscordLogEntry(Timestamp, LogLevel.Warning,
            "GeoClubBot.Infrastructure.Jobs.ActivityCheckerJob",
            "Failed to fetch club activity",
            "TimeoutException: The operation timed out");

        var embed = _formatter.Build(entry);

        embed.Title.Should().Be("⚠️ Warning · ActivityCheckerJob");
        embed.Color.Should().Be(new Color(0xE6, 0x7E, 0x22));
        embed.Description.Should().Be("Failed to fetch club activity\nTimeoutException: The operation timed out");
        embed.Footer!.Value.Text.Should().Be("GeoClubBot.Infrastructure.Jobs.ActivityCheckerJob");
        embed.Timestamp.Should().Be(Timestamp);
    }

    [Fact]
    public void Build_ErrorWithoutException_OmitsExceptionLineAndUsesErrorColor()
    {
        var entry = new DiscordLogEntry(Timestamp, LogLevel.Error, "MyApp.Service",
            "Unhandled failure in request", ExceptionLine: null);

        var embed = _formatter.Build(entry);

        embed.Title.Should().Be("❌ Error · Service");
        embed.Color.Should().Be(new Color(0xE7, 0x4C, 0x3C));
        embed.Description.Should().Be("Unhandled failure in request");
    }

    [Fact]
    public void Build_LongMessage_IsTruncatedWithinDiscordLimit()
    {
        var entry = new DiscordLogEntry(Timestamp, LogLevel.Critical, "MyApp",
            new string('x', 5000), ExceptionLine: null);

        var embed = _formatter.Build(entry);

        embed.Description!.Length.Should().BeLessThanOrEqualTo(4096);
        embed.Description.Should().EndWith("…");
        embed.Color.Should().Be(new Color(0x99, 0x2D, 0x22));
    }
}

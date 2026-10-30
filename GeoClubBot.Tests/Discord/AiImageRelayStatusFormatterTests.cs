using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// The <c>/ai status</c> line for the relay's public URL. On 2026-10-22 that URL stopped resolving and
/// nothing said so; the line exists to say so.
/// </summary>
public sealed class AiImageRelayStatusFormatterTests
{
    [Fact]
    public void Describe_SaysWhenNothingIsConfigured()
    {
        AiImageRelayStatusFormatter.Describe(
                new ImageRelayReachability(ImageRelayReachabilityStatus.NotConfigured, null, null))
            .Should().StartWith("not configured").And.Contain("AI:ImageRelay:PublicBaseUrl");
    }

    [Fact]
    public void Describe_NamesTheAddressThatAnswered()
    {
        AiImageRelayStatusFormatter.Describe(new ImageRelayReachability(
                ImageRelayReachabilityStatus.Reachable, "https://host.tailnet.ts.net", "answered in 85 ms"))
            .Should().Be("✅ `https://host.tailnet.ts.net` answered `/health` from the bot's host (answered in 85 ms)");
    }

    [Fact]
    public void Describe_SaysWhyAnAddressIsUnreachable_AndWhatStillWorks()
    {
        var line = AiImageRelayStatusFormatter.Describe(new ImageRelayReachability(
            ImageRelayReachabilityStatus.Unreachable, "https://host.tailnet.ts.net",
            "Name or service not known (host.tailnet.ts.net:443)"));

        line.Should().StartWith("❌ `https://host.tailnet.ts.net` is unreachable: Name or service not known");
        line.Should().Contain("still uploaded");
    }

    [Fact]
    public void Describe_KeepsALongReasonWithinAnEmbedField()
    {
        // Discord rejects the whole status embed when one field runs past 1024 characters.
        var line = AiImageRelayStatusFormatter.Describe(new ImageRelayReachability(
            ImageRelayReachabilityStatus.Unreachable, "https://host.tailnet.ts.net", new string('x', 5_000)));

        line.Length.Should().BeLessThan(1024);
    }

    [Fact]
    public void Describe_SaysUnknown_WhenThereIsNoAnswer()
    {
        AiImageRelayStatusFormatter.Describe(null).Should().Be("unknown");
    }
}

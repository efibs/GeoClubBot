using FluentAssertions;
using GeoClubBot.Services;
using Xunit;

namespace GeoClubBot.Tests.Api;

public sealed class AiConversationGatewayTests
{
    private const ulong BotUserId = 100000000000000001;

    [Theory]
    // "@Bot, where can I find Khasi Pines?" was stored, embedded and replayed as ", where can I…".
    [InlineData("<@100000000000000001>, where can I find Khasi Pines?", "where can I find Khasi Pines?")]
    [InlineData("<@!100000000000000001>: is this Bengali?", "is this Bengali?")]
    [InlineData("  <@100000000000000001>  ;  what cactus is this?", "what cactus is this?")]
    [InlineData("where is this, <@100000000000000001>", "where is this,")]
    [InlineData("<@100000000000000001>", "")]
    // Only the punctuation that addressed the bot goes: a reply that starts with ":)" keeps it.
    [InlineData(":) thanks, and the poles?", ":) thanks, and the poles?")]
    [InlineData("<@42>, is this Ghana?", "<@42>, is this Ghana?")]
    public void CleanContent_RemovesTheMentionAndWhatAddressedIt(string content, string expected) =>
        AiConversationGateway.CleanContent(content, BotUserId).Should().Be(expected);
}

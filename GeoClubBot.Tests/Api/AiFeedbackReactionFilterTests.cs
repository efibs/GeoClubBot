using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Services;
using Xunit;

namespace GeoClubBot.Tests.Api;

/// <summary>
/// The gate every reaction in the guild passes through. It runs before any I/O, so each rung matters
/// for load as much as for correctness — and the bot-self rung is not an edge case at all once the
/// bot prefills 👍/👎 on its own answers.
/// </summary>
public sealed class AiFeedbackReactionFilterTests
{
    private const ulong BotUserId = 1;
    private const ulong ChannelId = 5;

    [Theory]
    [InlineData("👍", AiFeedbackRating.Positive)]
    [InlineData("👎", AiFeedbackRating.Negative)]
    public void Classify_RecognisesTheConfiguredVerdicts(string emoji, AiFeedbackRating expected)
    {
        Classify(Signal(emoteName: emoji)).Should().Be(expected);
    }

    [Theory]
    [InlineData("🎉")]
    [InlineData("👍🏽")]
    [InlineData("thumbsup")]
    public void Classify_IgnoresEveryOtherReaction(string emoji)
    {
        // The first rung, and the one that discards nearly all traffic — a skin-tone variant is a
        // different codepoint, so it is simply not one of the two votes.
        Classify(Signal(emoteName: emoji)).Should().BeNull();
    }

    [Fact]
    public void Classify_IgnoresTheBotsOwnReactions()
    {
        // Prefilling makes this the common case: every answer the bot decorates raises two events.
        Classify(Signal(userId: BotUserId)).Should().BeNull();
    }

    [Fact]
    public void Classify_IgnoresOtherBots()
    {
        Classify(Signal(userId: 99, userIsBot: true)).Should().BeNull();
    }

    [Fact]
    public void Classify_IgnoresDirectMessages()
    {
        // Mirrors the answering side: a DM bypasses the channel allowlist entirely.
        Classify(Signal(isGuildChannel: false)).Should().BeNull();
    }

    [Fact]
    public void Classify_IgnoresChannelsOutsideTheAllowlist()
    {
        var ai = new AiConfiguration { AllowedChannelIds = [999] };

        Classify(Signal(), ai).Should().BeNull();
    }

    [Fact]
    public void Classify_AcceptsAnyChannel_WhenTheAllowlistIsEmpty()
    {
        Classify(Signal(), new AiConfiguration { AllowedChannelIds = [] }).Should().NotBeNull();
    }

    [Fact]
    public void Classify_IgnoresEverything_WhenFeedbackIsSwitchedOff()
    {
        Classify(Signal(), feedback: new AiFeedbackConfiguration { Enabled = false }).Should().BeNull();
    }

    [Fact]
    public void Classify_HonoursReconfiguredEmoji()
    {
        var feedback = new AiFeedbackConfiguration { PositiveEmoji = "✅", NegativeEmoji = "❌" };

        Classify(Signal(emoteName: "✅"), feedback: feedback).Should().Be(AiFeedbackRating.Positive);
        Classify(Signal(emoteName: "👍"), feedback: feedback).Should().BeNull();
    }

    private static AiFeedbackRating? Classify(
        ReactionSignal signal,
        AiConfiguration? ai = null,
        AiFeedbackConfiguration? feedback = null) =>
        AiFeedbackReactionFilter.Classify(
            signal, BotUserId, ai ?? new AiConfiguration(), feedback ?? new AiFeedbackConfiguration());

    private static ReactionSignal Signal(
        string emoteName = "👍",
        ulong userId = 42,
        bool userIsBot = false,
        bool isGuildChannel = true) =>
        new(emoteName, userId, userIsBot, isGuildChannel, ChannelId);
}

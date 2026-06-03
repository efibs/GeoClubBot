using Extensions;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.ExtensionMethods;

public sealed class StringExtensionsTests
{
    [Fact]
    public void SplitAtCharWithLimit_ShortInput_ReturnsSingleChunk()
    {
        var result = "a b c".SplitAtCharWithLimit(" ", 100);

        result.Should().ContainSingle().Which.Should().Be("a b c");
    }

    [Fact]
    public void SplitAtCharWithLimit_BreaksIntoChunksUnderLimit()
    {
        var result = "a b c d".SplitAtCharWithLimit(" ", 4);

        result.Should().Equal("a b", "c d");
        result.Should().OnlyContain(chunk => chunk.Length <= 4);
    }

    [Fact]
    public void SplitAtCharWithLimit_SingleTokenLongerThanLimit_IsKeptWhole()
    {
        // A token that cannot fit is emitted as its own (oversized) chunk rather than truncated.
        var result = "aaaaaaaa".SplitAtCharWithLimit(" ", 3);

        result.Should().ContainSingle().Which.Should().Be("aaaaaaaa");
    }

    [Fact]
    public void SplitAtCharWithLimit_EmptyString_ReturnsEmptyList()
    {
        var result = "".SplitAtCharWithLimit(" ", 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public void SplitAtCharWithLimit_TrimsTrailingSeparatorOnFlushedChunks()
    {
        var result = "one two three".SplitAtCharWithLimit(" ", 8);

        result.Should().OnlyContain(chunk => chunk == chunk.TrimEnd());
    }

    [Fact]
    public void SplitAtCharWithLimit_PreservesAllTokens()
    {
        var result = "alpha beta gamma delta".SplitAtCharWithLimit(" ", 12);

        string.Join(" ", result).Split(' ')
            .Should().Equal("alpha", "beta", "gamma", "delta");
    }

    [Fact]
    public void SplitAtCharWithLimit_SupportsMultiCharSeparator()
    {
        var result = "a::bb::ccc".SplitAtCharWithLimit("::", 6);

        result.Should().OnlyContain(chunk => chunk.Length <= 6);
        string.Join("::", result).Replace("::", "").Should().Be("abbccc");
    }

    [Theory]
    [InlineData("DE", "🇩🇪")]
    [InlineData("us", "🇺🇸")]
    [InlineData(" fr ", "🇫🇷")]
    public void ToFlagEmoji_ValidCountryCode_ReturnsRegionalIndicatorPair(string code, string expected)
    {
        code.ToFlagEmoji().Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("D")]
    [InlineData("DEU")]
    [InlineData("D1")]
    public void ToFlagEmoji_InvalidInput_ReturnsEmptyString(string? code)
    {
        code.ToFlagEmoji().Should().BeEmpty();
    }
}

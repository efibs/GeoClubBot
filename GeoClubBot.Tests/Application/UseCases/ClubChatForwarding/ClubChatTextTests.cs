using FluentAssertions;
using UseCases.UseCases.ClubChatForwarding;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Application.UseCases.ClubChatForwarding;

public sealed class ClubChatTextSplitterTests
{
    [Fact]
    public void AShortText_IsOneUnnumberedMessage()
    {
        var parts = ClubChatTextSplitter.Split("  Hello club!  ", 300, 5);

        parts.Value.Should().Equal("Hello club!");
    }

    [Fact]
    public void AnEmptyText_IsRefused()
    {
        ClubChatTextSplitter.Split(" \n ", 300, 5).Error.Code.Should().Be("club-chat.empty");
    }

    [Fact]
    public void ALongText_IsSplitAtParagraphs_AndNumbered()
    {
        var first = new string('a', 200);
        var second = new string('b', 200);

        var parts = ClubChatTextSplitter.Split($"{first}\n\n{second}", 300, 5).Value;

        parts.Should().Equal($"(1/2) {first}", $"(2/2) {second}");
    }

    [Fact]
    public void WithoutParagraphs_ItSplitsAtSentences()
    {
        var sentence = string.Join(' ', Enumerable.Repeat("word", 20)) + ".";
        var text = string.Join(' ', Enumerable.Repeat(sentence, 4));

        var parts = ClubChatTextSplitter.Split(text, 300, 5).Value;

        parts.Should().HaveCount(2);
        parts[0].Should().EndWith(".");
        parts.Should().OnlyContain(p => p.Length <= 300);
    }

    [Fact]
    public void AWordLongerThanAMessage_IsCut()
    {
        var parts = ClubChatTextSplitter.Split(new string('x', 650), 300, 5).Value;

        parts.Should().HaveCount(3).And.OnlyContain(p => p.Length <= 300);
        string.Concat(parts.Select(p => p[6..])).Should().Be(new string('x', 650));
    }

    [Fact]
    public void ATextNeedingTooManyParts_IsRefused_SayingByHowMuch()
    {
        var result = ClubChatTextSplitter.Split(string.Join(' ', Enumerable.Repeat("word", 400)), 300, 5);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("club-chat.too_long");
        result.Error.Message.Should().Contain("at most 5").And.Contain("Shorten it by about");
    }

    [Fact]
    public void AnEmojiIsNeverCutInHalf()
    {
        var text = new string('x', 293) + "😀😀😀😀😀😀😀😀";

        var parts = ClubChatTextSplitter.Split(text, 300, 5).Value;

        parts.Should().OnlyContain(p => !char.IsHighSurrogate(p[p.Length - 1]) && !char.IsLowSurrogate(p[6]));
    }
}

public sealed class ClubChatTemplateTests
{
    [Fact]
    public void FillsEveryPlaceholder()
    {
        ClubChatTemplate.Render("{text}\n\nForwarded by {author} from #{channel}", "Hi", "Fibs", "announcements")
            .Should().Be("Hi\n\nForwarded by Fibs from #announcements");
    }

    [Fact]
    public void LeavesPlaceholdersWrittenInTheMessageAlone()
    {
        ClubChatTemplate.Render("{text} ({author})", "Type {author} here", "Fibs", "x")
            .Should().Be("Type {author} here (Fibs)");
    }
}

public sealed class DiscordTextFlattenerTests
{
    private static readonly DiscordNameLookup Names = new(
        id => id == 1 ? "Fibs" : null,
        id => id == 2 ? "Members" : null,
        id => id == 3 ? "general" : null);

    [Theory]
    [InlineData("Hi <@1> and <@!1>", "Hi @Fibs and @Fibs")]
    [InlineData("<@9>", "@unknown-user")]
    [InlineData("<@&2>, see <#3>", "@Members, see #general")]
    [InlineData("Nice <:geo:123> <a:spin:456>", "Nice :geo: :spin:")]
    [InlineData("Starts <t:1767225600:F>", "Starts 2026-01-01 00:00 UTC")]
    [InlineData("On <t:1767225600:D>", "On 2026-01-01")]
    [InlineData("**Bold** __under__ ~~gone~~ ||secret||", "Bold under gone secret")]
    [InlineData("*italic* and _also_", "italic and also")]
    [InlineData("keep snake_case_names and 2*3*4", "keep snake_case_names and 2*3*4")]
    [InlineData("# Title\n## Sub\n-# small", "Title\nSub\nsmall")]
    [InlineData("> quoted\nnot", "quoted\nnot")]
    [InlineData(">>> all\n> of it", "all\n> of it")]
    [InlineData("`**code**`", "**code**")]
    [InlineData("[the guide](https://example.com/a_b_c)", "the guide (https://example.com/a_b_c)")]
    [InlineData("<https://example.com/x__y>", "https://example.com/x__y")]
    [InlineData("https://example.com/**x**", "https://example.com/**x**")]
    [InlineData(@"\*not italic\*", "*not italic*")]
    [InlineData("Use </ai ask:99>", "Use /ai ask")]
    [InlineData("a\n\n\n\nb", "a\n\nb")]
    public void Flattens(string content, string expected)
    {
        DiscordTextFlattener.Flatten(content, Names).Should().Be(expected);
    }

    [Fact]
    public Task AnAnnouncement_ReadsAsPlainText()
    {
        const string announcement = """
            # 📢 Club update
            Hey <@&2>! **This week** we play the _country challenge_ in <#3>.

            > Deadline: <t:1767225600:f>
            - Join with </join:1>
            - Read [the rules](https://example.com/rules) first ||or else||

            ```
            code stays *as is*
            ```
            Thanks <@1> <:geo:123>
            """;

        return Verify(DiscordTextFlattener.Flatten(announcement, Names));
    }
}

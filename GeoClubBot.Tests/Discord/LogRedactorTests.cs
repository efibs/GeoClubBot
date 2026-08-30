using FluentAssertions;
using GeoClubBot.Discord.Logging;
using Xunit;

namespace GeoClubBot.Tests.Discord;

public sealed class LogRedactorTests
{
    private const string Redacted = "***REDACTED***";

    [Fact]
    public void Redact_NcfaCookie_RemovesValueButKeepsName()
    {
        var input = "Request failed with Cookie: _ncfa=abc123DEF456%2Bxyz; other=1";

        var result = LogRedactor.Redact(input);

        result.Should().Contain("_ncfa=" + Redacted);
        result.Should().NotContain("abc123DEF456");
        result.Should().Contain("other=1"); // unrelated trailing cookie untouched
    }

    [Fact]
    public void Redact_DiscordBotToken_IsRemoved()
    {
        // Synthetic token: shaped like a Discord bot token so the regex fires, but obviously fake
        // (repeated literals, leading 'F') so it is neither a real secret nor flagged by scanners.
        const string token = "FAKEFAKEFAKEFAKEFAKEFAKE.TOKEN1.FAKEFAKEFAKEFAKEFAKEFAKEFAKE";
        var input = $"Logging in with token {token}";

        var result = LogRedactor.Redact(input);

        result.Should().NotContain(token);
        result.Should().Contain(Redacted);
    }

    [Fact]
    public void Redact_CerebrasApiKey_IsRemoved()
    {
        const string key = "csk-FAKEfakeFAKEfake1234567890"; // synthetic, not a real key
        var input = $"AI call failed using key={key}";

        var result = LogRedactor.Redact(input);

        result.Should().NotContain(key);
        result.Should().Contain(Redacted);
    }

    [Fact]
    public void Redact_ConnectionStringPassword_KeepsKeyRedactsValue()
    {
        var input = "Host=localhost;Database=geoclubbot;Username=admin;Password=sup3rSecret";

        var result = LogRedactor.Redact(input);

        result.Should().Contain("Password=" + Redacted);
        result.Should().NotContain("sup3rSecret");
        result.Should().Contain("Username=admin"); // non-secret fields untouched
    }

    [Theory]
    [InlineData("\"token\": \"abcd1234efgh\"")]
    [InlineData("api_key=abcd1234efgh")]
    [InlineData("ApiKey = abcd1234efgh")]
    [InlineData("secret: abcd1234efgh")]
    public void Redact_KeyedSecretAssignments_RedactValue(string input)
    {
        var result = LogRedactor.Redact(input);

        result.Should().NotContain("abcd1234efgh");
        result.Should().Contain(Redacted);
    }

    [Theory]
    [InlineData("Failed to fetch club activity")]
    [InlineData("The token expired and the password reset email was sent")]
    [InlineData("Processed 42 members in 1.3s")]
    public void Redact_OrdinaryMessages_AreUnchanged(string input)
    {
        // No assignment syntax and no secret shapes — must pass through verbatim so logs stay useful.
        LogRedactor.Redact(input).Should().Be(input);
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsInput()
    {
        LogRedactor.Redact("").Should().Be("");
    }
}

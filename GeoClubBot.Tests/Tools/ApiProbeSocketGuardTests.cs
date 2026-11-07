using FluentAssertions;
using GeoClubBot.ApiProbe;
using Xunit;

namespace GeoClubBot.Tests.Tools;

/// <summary>
/// The API probe's socket command is pointed at real club accounts, so it must only ever listen:
/// subscribe to a club's topic and send the heartbeat, nothing that would act in the club.
/// </summary>
public sealed class ApiProbeSocketGuardTests
{
    [Theory]
    [InlineData("""{"code":"Subscribe","topic":"club:d0c6b623-8df6-4654-860d-a4757cf78cf4","client":"web"}""")]
    [InlineData("""{"code":"HeartBeat"}""")]
    public void ListeningFrames_AreAllowed(string frame)
    {
        var send = () => ReadOnlySocketGuard.EnsureAllowed(frame);

        send.Should().NotThrow();
    }

    [Theory]
    [InlineData("""{"code":"ChatMessage","topic":"chat:Club:TextMessages:d0c6b623-8df6-4654-860d-a4757cf78cf4","payload":"hi","client":"web"}""")]
    [InlineData("""{"code":"ChatReaction","topic":"chat:Club:TextMessages:d0c6b623-8df6-4654-860d-a4757cf78cf4","payload":"x","client":"web"}""")]
    [InlineData("""{"code":"Subscribe","topic":"chat:Club:TextMessages:d0c6b623-8df6-4654-860d-a4757cf78cf4","client":"web"}""")]
    [InlineData("""{"code":"Subscribe","topic":"self:5a8b","client":"web"}""")]
    [InlineData("""{"code":"Subscribe","topic":"club:d0c6b623-8df6-4654-860d-a4757cf78cf4","client":"web","payload":"x"}""")]
    [InlineData("""{"code":"HeartBeat","payload":"x"}""")]
    [InlineData("""{"code":"heartbeat"}""")]
    [InlineData("""["HeartBeat"]""")]
    [InlineData("HeartBeat")]
    public void AnythingElse_IsRefused(string frame)
    {
        var send = () => ReadOnlySocketGuard.EnsureAllowed(frame);

        send.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TheFramesAccessToken_IsRedacted_LeavingValidJson()
    {
        var frame = """{"code":"ClubUpdated","payload":"{}","accessToken":"eyJ.secret\"value","client":"web"}""";

        var redacted = TokenRedactor.Redact(frame);

        redacted.Should().Be("""{"code":"ClubUpdated","payload":"{}","accessToken":"***REDACTED***","client":"web"}""");
        System.Text.Json.JsonDocument.Parse(redacted).Dispose();
    }

    [Fact]
    public void TheCookie_IsStillRedacted() =>
        TokenRedactor.Redact("Cookie: _ncfa=abc%2Fdef; other=1").Should().Be("Cookie: _ncfa=***REDACTED***; other=1");
}

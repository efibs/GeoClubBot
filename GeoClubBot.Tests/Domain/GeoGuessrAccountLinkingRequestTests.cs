using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

public sealed class GeoGuessrAccountLinkingRequestTests
{
    [Fact]
    public void Create_SetsAllFields()
    {
        var request = GeoGuessrAccountLinkingRequest.Create(123UL, "ggp-1", "ABC123");

        request.DiscordUserId.Should().Be(123UL);
        request.GeoGuessrUserId.Should().Be("ggp-1");
        request.OneTimePassword.Should().Be("ABC123");
    }

    [Fact]
    public void Matches_ReturnsTrue_ForExactPassword()
    {
        var request = GeoGuessrAccountLinkingRequest.Create(1UL, "u", "secret");

        request.Matches("secret").Should().BeTrue();
    }

    [Fact]
    public void Matches_IsCaseSensitive()
    {
        var request = GeoGuessrAccountLinkingRequest.Create(1UL, "u", "Secret");

        request.Matches("secret").Should().BeFalse();
    }

    [Fact]
    public void Matches_ReturnsFalse_ForWrongPassword()
    {
        var request = GeoGuessrAccountLinkingRequest.Create(1UL, "u", "secret");

        request.Matches("nope").Should().BeFalse();
    }
}

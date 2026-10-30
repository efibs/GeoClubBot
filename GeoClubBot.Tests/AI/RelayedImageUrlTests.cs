using FluentAssertions;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The shape of a relayed image's URL, which the relay writes, the API serves and the answer path
/// recognises. A URL from an old hostname must not be shown as-is: on 2026-10-22 the stored host
/// stopped resolving and every relayed picture failed to show.
/// </summary>
public sealed class RelayedImageUrlTests
{
    private static readonly string StoredName = new string('a', 64) + ".png";

    [Fact]
    public void Rebase_MovesARelayedUrlFromAnOldHostOntoTheCurrentBase()
    {
        var rebased = RelayedImageUrl.Rebase(
            $"https://old-tunnel.trycloudflare.com/api/v1/ai/images/{StoredName}", "https://host.tailnet.ts.net");

        rebased.Should().Be($"https://host.tailnet.ts.net/api/v1/ai/images/{StoredName}");
    }

    [Theory]
    [InlineData("https://host.example.net/")]
    [InlineData("  https://host.example.net  ")]
    public void Rebase_ToleratesATrailingSlashOrSpaceInTheBase(string publicBaseUrl)
    {
        RelayedImageUrl.Rebase($"https://old.example.net/api/v1/ai/images/{StoredName}", publicBaseUrl)
            .Should().Be($"https://host.example.net/api/v1/ai/images/{StoredName}");
    }

    [Fact]
    public void Rebase_KeepsABaseThatHasAPathOfItsOwn()
    {
        // Behind a reverse proxy the bot may live under a path prefix, which the route sits beneath.
        RelayedImageUrl.Rebase($"https://old.example.net/bot/api/v1/ai/images/{StoredName}", "https://proxy.example.net/geo")
            .Should().Be($"https://proxy.example.net/geo/api/v1/ai/images/{StoredName}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rebase_LeavesTheUrlAlone_WhenNoBaseIsConfigured(string? publicBaseUrl)
    {
        var stored = $"https://old.example.net/api/v1/ai/images/{StoredName}";

        RelayedImageUrl.Rebase(stored, publicBaseUrl).Should().Be(stored);
    }

    [Theory]
    [InlineData("host.tailnet.ts.net")]
    [InlineData("ftp://host.tailnet.ts.net")]
    [InlineData("/api")]
    public void Rebase_KeepsTheStoredUrl_WhenTheBaseIsNotAnHttpUrl(string publicBaseUrl)
    {
        // Discord rejects a whole message whose embed points at a scheme-less URL, so a mistyped base
        // must never replace a link that works.
        var stored = $"https://old.example.net/api/v1/ai/images/{StoredName}";

        RelayedImageUrl.Rebase(stored, publicBaseUrl).Should().Be(stored);
    }

    [Theory]
    [InlineData("https://i.imgur.com/abc.png")]
    [InlineData("https://assets.rmrg.me/guides/brazil/poles.webp")]
    [InlineData("https://old.example.net/api/v1/ai/images/not-a-hash.png")]
    [InlineData("https://old.example.net/api/v1/ai/images/0000000000000000000000000000000000000000000000000000000000000000.exe")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("not a url")]
    public void Rebase_LeavesAnythingThatIsNotARelayedImageUntouched(string imageUrl)
    {
        RelayedImageUrl.Rebase(imageUrl, "https://host.example.net").Should().Be(imageUrl);
    }

    [Theory]
    [InlineData("https://host.tailnet.ts.net/api/v1/ai/images/{0}")]
    [InlineData("http://192.168.1.10:8080/api/v1/ai/images/{0}")]
    [InlineData("https://old.example.net/api/v1/ai/images/{0}?v=2")]
    public void TryReadName_FindsTheStoredNameWhateverTheHost(string format)
    {
        RelayedImageUrl.TryReadName(string.Format(format, StoredName), out var name).Should().BeTrue();

        name.Should().Be(StoredName);
    }

    [Theory]
    [InlineData("https://host.example.net/api/v1/ai/images/")]
    [InlineData("https://host.example.net/api/v1/ai/images/../../../etc/passwd")]
    [InlineData("https://host.example.net/api/v1/ai/images/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.png")]
    [InlineData("ftp://host.example.net/api/v1/ai/images/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png")]
    [InlineData("https://host.example.net/images/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png")]
    [InlineData(null)]
    public void TryReadName_RefusesAnythingThatIsNotAStoredImage(string? imageUrl)
    {
        // Only an exact content hash with a known extension can ever reach a path on disk.
        RelayedImageUrl.TryReadName(imageUrl, out _).Should().BeFalse();
    }

    [Fact]
    public void Build_WritesAUrlThatReadsBackToTheSameName()
    {
        var url = RelayedImageUrl.Build("https://host.example.net/", StoredName);

        url.Should().Be($"https://host.example.net/{RelayedImageUrl.RoutePrefix}/{StoredName}");
        RelayedImageUrl.TryReadName(url, out var name).Should().BeTrue();
        name.Should().Be(StoredName);
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("IMAGE/JPG", ".jpg")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/svg+xml", null)]
    [InlineData(null, null)]
    public void ExtensionFor_NamesOnlyTheTypesTheRelayStores(string? contentType, string? expected)
    {
        // An allowlist: these bytes are served back from our own origin under the type we declare.
        RelayedImageUrl.ExtensionFor(contentType).Should().Be(expected);
    }
}

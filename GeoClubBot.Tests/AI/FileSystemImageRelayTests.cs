using System.Net;
using System.Net.Http.Headers;
using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.ImageRelay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The relay is the one anonymous, publicly reachable surface the AI feature adds, so its refusals
/// matter as much as its successes.
/// </summary>
public sealed class FileSystemImageRelayTests : IDisposable
{
    /// <summary>A one-pixel PNG: real magic bytes, so type sniffing has something honest to read.</summary>
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01
    ];

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"relay-{Guid.NewGuid():N}");

    [Fact]
    public async Task Store_ReturnsAUrlUnderTheConfiguredPublicBase()
    {
        // The base URL is configuration rather than something discovered, because behind a tunnel the
        // bot only ever sees an internal address.
        var relay = Create();

        var result = await relay.StoreAsync(PngBytes, "image/png");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("https://host.tailnet.ts.net/api/v1/ai/images/");
        result.Value.Should().EndWith(".png");
    }

    [Fact]
    public async Task Store_IsContentAddressed_SoTheSameImageIsKeptOnce()
    {
        var relay = Create();

        var first = await relay.StoreAsync(PngBytes, "image/png");
        var second = await relay.StoreAsync(PngBytes, "image/png");

        second.Value.Should().Be(first.Value);
        Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).Should().ContainSingle();
    }

    [Fact]
    public async Task Store_TrustsTheBytesOverTheDeclaredType()
    {
        // Guide hosts routinely serve PNGs as octet-stream, and the type we record is the one a
        // browser will later act on.
        var relay = Create();

        var result = await relay.StoreAsync(PngBytes, "application/octet-stream");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().EndWith(".png");
    }

    [Fact]
    public async Task Store_RefusesSomethingThatIsNotAnImage()
    {
        var relay = Create();

        var result = await relay.StoreAsync("<html>not an image</html>"u8.ToArray(), "text/html");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.image_unsupported_type");
    }

    [Fact]
    public async Task Store_RefusesAnImageOverTheSizeLimit()
    {
        var relay = Create(maxBytes: 16);

        var result = await relay.StoreAsync(PngBytes, "image/png");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ai.image_too_large");
    }

    [Fact]
    public async Task Read_ReturnsTheStoredBytesWithTheirType()
    {
        var relay = Create();
        var stored = await relay.StoreAsync(PngBytes, "image/png");
        var name = stored.Value[(stored.Value.LastIndexOf('/') + 1)..];

        var image = await relay.ReadAsync(name);

        image.Should().NotBeNull();
        image!.ContentType.Should().Be("image/png");

        using var buffer = new MemoryStream();
        await image.Content.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(PngBytes);
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..%2f..%2fsecret.png")]
    [InlineData("not-a-hash.png")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000.exe")]
    [InlineData("")]
    public async Task Read_RefusesAnythingThatIsNotAContentHash(string name)
    {
        // The served name is validated before it can reach a path, so no caller-supplied text ever
        // takes part in building one.
        var relay = Create();

        (await relay.ReadAsync(name)).Should().BeNull();
    }

    [Fact]
    public async Task Resolve_LeavesAloneWhatTheProviderCanAlreadyFetch()
    {
        // Copying someone's images is a bigger imposition than linking them, so only hosts known to
        // refuse unattended clients are relayed.
        var relay = Create();

        var resolved = await relay.ResolveAsync("https://i.imgur.com/abc.png");

        resolved.Should().Be("https://i.imgur.com/abc.png");
        Directory.Exists(_directory).Should().BeFalse("nothing should have been downloaded");
    }

    [Fact]
    public async Task Resolve_CopiesAnImageFromABlockedHost()
    {
        var relay = Create(new StubHandler(HttpStatusCode.OK, PngBytes, "image/png"));

        var resolved = await relay.ResolveAsync("https://www.plonkit.net/images/tunisia/plate.png");

        resolved.Should().StartWith("https://host.tailnet.ts.net/api/v1/ai/images/");
    }

    [Fact]
    public async Task Resolve_KeepsTheOriginalUrl_WhenTheHostStillRefusesUs()
    {
        // Measured behaviour of at least one guide CDN. A missing picture must not cost the source it
        // belongs to, so the original link is kept and indexing continues.
        var relay = Create(new StubHandler(HttpStatusCode.Forbidden, [], null));

        var resolved = await relay.ResolveAsync("https://www.plonkit.net/images/tunisia/plate.png");

        resolved.Should().Be("https://www.plonkit.net/images/tunisia/plate.png");
    }

    [Fact]
    public async Task Resolve_IsInert_WhenNoPublicBaseUrlIsConfigured()
    {
        // Without a reachable base URL a relayed link would be useless, so nothing is copied at all.
        var relay = Create(publicBaseUrl: null);

        relay.IsEnabled.Should().BeFalse();
        (await relay.ResolveAsync("https://www.plonkit.net/images/x.png"))
            .Should().Be("https://www.plonkit.net/images/x.png");
    }

    private FileSystemImageRelay Create(
        StubHandler? handler = null,
        string? publicBaseUrl = "https://host.tailnet.ts.net",
        int maxBytes = 8 * 1024 * 1024)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(FileSystemImageRelay.HttpClientName)
            .Returns(new HttpClient(handler ?? new StubHandler(HttpStatusCode.OK, PngBytes, "image/png")));

        return new FileSystemImageRelay(
            factory,
            Options.Create(new AiImageRelayConfiguration
            {
                PublicBaseUrl = publicBaseUrl,
                Directory = _directory,
                RelayHosts = ["plonkit.net"],
                MaxImageBytes = maxBytes
            }),
            NullLogger<FileSystemImageRelay>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class StubHandler(HttpStatusCode status, byte[] body, string? contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(body);
            if (contentType is not null)
            {
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }

            return Task.FromResult(new HttpResponseMessage(status) { Content = content, RequestMessage = request });
        }
    }
}

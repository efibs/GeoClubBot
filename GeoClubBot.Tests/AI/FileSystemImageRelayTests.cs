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
    public async Task Resolve_RewritesTheReferer_WhenAHostRedirectsToItsMirror()
    {
        // Measured on plonkit.net: www.plonkit.net sends the request on to a regional mirror, which
        // then serves the image only if the referer names the mirror too. Carrying the original
        // referer across the hop reads as hotlinking and gets a 403, so the relay re-derives it.
        var handler = new MirrorHandler(PngBytes);
        var relay = Create(handler);

        var resolved = await relay.ResolveAsync("https://www.plonkit.net/images/tunisia/plate.png");

        resolved.Should().StartWith("https://host.tailnet.ts.net/api/v1/ai/images/");
        handler.LastReferrer.Should().Be("https://de.plonkit.net");
    }

    [Fact]
    public async Task Resolve_GivesUpOnARedirectLoop()
    {
        var relay = Create(new LoopingHandler());

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
        HttpMessageHandler? handler = null,
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

    /// <summary>
    /// Mimics a host that redirects to a regional mirror and then requires the referer to name that
    /// mirror rather than the site the redirect came from.
    /// </summary>
    private sealed class MirrorHandler(byte[] body) : HttpMessageHandler
    {
        public string? LastReferrer { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastReferrer = request.Headers.Referrer?.GetLeftPart(UriPartial.Authority);

            if (request.RequestUri!.Host == "www.plonkit.net")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
                redirect.Headers.Location =
                    new Uri($"https://de.plonkit.net{request.RequestUri.PathAndQuery}");
                return Task.FromResult(redirect);
            }

            if (LastReferrer != $"https://{request.RequestUri.Host}")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new ByteArrayContent([]),
                    RequestMessage = request
                });
            }

            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
                RequestMessage = request
            });
        }
    }

    /// <summary>Redirects for ever, so the hop limit is what has to end the fetch.</summary>
    private sealed class LoopingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
            redirect.Headers.Location = new Uri("https://www.plonkit.net/images/again.png");
            return Task.FromResult(redirect);
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

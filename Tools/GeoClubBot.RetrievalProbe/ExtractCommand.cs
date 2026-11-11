using System.Net;
using System.Text;
using Configuration;
using Infrastructure.OutputAdapters.AI.Extractors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Utilities;
using Match = Qdrant.Client.Grpc.Match;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// What a guide would be indexed as, read now by the bot's own extractor and chunker, without indexing
/// anything. For changing how a source family is read — which lines are headings, what captions a
/// picture — and looking at the result on a real guide before a night's run re-embeds the library.
///
/// The guide's page is fetched from its host, as the nightly run fetches it. Its pictures are not: a
/// request for one is answered here, so a document of a thousand pictures reads in a second, and each
/// picture chunk shows the link it would have been downloaded from. Nothing is stored anywhere.
/// </summary>
public static class ExtractCommand
{
    private const string UserAgent = "GeoClubBot/1.0 (+https://github.com/efibs/geo-club-bot)";

    public static async Task RunAsync(ProbeContext context)
    {
        if (context.Arguments.Target is not { Length: > 0 } link || !Uri.TryCreate(link, UriKind.Absolute, out var url))
        {
            throw new ProbeException("extract needs a guide's link, e.g. extract https://docs.google.com/document/d/<id>/edit");
        }

        var classified = SourceLinkClassifier.Classify(url);
        if (classified.UnsupportedReason is { } reason)
        {
            throw new ProbeException($"The bot does not index this link: {reason}");
        }

        using var http = new HttpClient(new PictureStubHandler(new HttpClientHandler()));
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.Timeout = TimeSpan.FromMinutes(5);

        var factory = new SingleClientFactory(http);
        var relay = new EchoRelay();
        var settings = Options.Create(new AiIngestionConfiguration());

        ISourceExtractor extractor = classified.SourceType switch
        {
            SourceLinkClassifier.GoogleDoc => new GoogleDocSourceExtractor(factory, relay, settings, NullLogger<GoogleDocSourceExtractor>.Instance),
            SourceLinkClassifier.GoogleSlides => new GoogleSlidesSourceExtractor(factory, relay, settings, NullLogger<GoogleSlidesSourceExtractor>.Instance),
            SourceLinkClassifier.GoogleSheet => new GoogleSheetSourceExtractor(factory),
            SourceLinkClassifier.PlonkIt => new PlonkItSourceExtractor(factory, NullLogger<PlonkItSourceExtractor>.Instance),
            SourceLinkClassifier.Rmrg => new RmrgSourceExtractor(factory, NullLogger<RmrgSourceExtractor>.Instance),
            SourceLinkClassifier.ImgurAlbum => new ImgurAlbumSourceExtractor(factory, NullLogger<ImgurAlbumSourceExtractor>.Instance),
            _ => throw new ProbeException($"extract cannot read '{classified.SourceType}' sources.")
        };

        // Titled and filed as the index has it, since both go into every chunk's section and vector.
        var (title, country) = await ReadIndexedDetailsAsync(context, classified.SourceType, classified.NaturalKey).ConfigureAwait(false);
        var descriptor = new SourceDescriptor(classified.SourceType, classified.NaturalKey, url, title, country);

        var extracted = await extractor.ExtractAsync(descriptor, context.CancellationToken).ConfigureAwait(false);
        if (extracted.IsFailure)
        {
            throw new ProbeException($"The extractor failed: {extracted.Error.Code}: {extracted.Error.Message}");
        }

        var chunks = ContentChunker.Chunk(extracted.Value.Chunks);
        var pictures = chunks.Count(chunk => chunk.ImageUrl is not null);

        var report = context.Report;
        report.Line($"# {classified.SourceType}:{classified.NaturalKey} as it would be indexed");
        report.Line();
        report.Line($"{title ?? "(not in the index: no title)"} · {country ?? "(no country)"} · {chunks.Count} chunk(s), {pictures} with a picture"
                    + (extracted.Value.Degraded ? " · degraded: a fuller reading failed" : string.Empty));

        foreach (var chunk in chunks.Take(context.Arguments.Max))
        {
            report.Line();
            report.Line($"### `{chunk.LocalKey}` {(chunk.ImageUrl is null ? "text" : "🖼")} · {HitDescriptions.Cell(chunk.SectionPath, 160)}"
                        + (chunk.Anchor is null ? string.Empty : $" · #{chunk.Anchor}"));
            if (chunk.ImageUrl is { } picture)
            {
                report.Line($"picture: {picture}");
            }

            report.Line();
            foreach (var line in chunk.Text.Split('\n'))
            {
                report.Line($"> {line.TrimEnd()}");
            }
        }

        if (chunks.Count > context.Arguments.Max)
        {
            report.Line();
            report.Line($"_{chunks.Count - context.Arguments.Max} more; raise --max to see them._");
        }
    }

    private static async Task<(string? Title, string? Country)> ReadIndexedDetailsAsync(
        ProbeContext context,
        string sourceType,
        string sourceKey)
    {
        var found = await context.Qdrant.ScrollAsync(
            context.Settings.Collection,
            filter: new Filter { Must = { Keyword("sourceType", sourceType), Keyword("sourceKey", sourceKey) } },
            limit: 1,
            payloadSelector: new WithPayloadSelector { Include = new PayloadIncludeSelector { Fields = { "title", "country" } } },
            vectorsSelector: false,
            cancellationToken: context.CancellationToken).ConfigureAwait(false);

        var payload = found.Result.FirstOrDefault()?.Payload;
        if (payload is null)
        {
            return (null, null);
        }

        return (NullIfEmpty(payload.TryGetValue("title", out var title) ? title.StringValue : null),
            NullIfEmpty(payload.TryGetValue("country", out var country) ? country.StringValue : null));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static Condition Keyword(string field, string value) =>
        new() { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } } };

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    /// <summary>
    /// Answers a request for a document's picture without sending it: the body is the picture's own
    /// link, which <see cref="EchoRelay"/> hands back as where it is "stored". Everything else — the
    /// guide's page — goes out as asked, and only as a GET.
    /// </summary>
    private sealed class PictureStubHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get)
            {
                throw new ProbeException($"The probe only reads: {request.Method} {request.RequestUri} was not sent.");
            }

            var url = request.RequestUri!;
            var isPicture = url.Host.EndsWith("googleusercontent.com", StringComparison.OrdinalIgnoreCase)
                            || url.AbsolutePath.Contains("/drawings/", StringComparison.OrdinalIgnoreCase);

            if (!isPicture)
            {
                return base.SendAsync(request, cancellationToken);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(url.AbsoluteUri)),
                RequestMessage = request
            });
        }
    }

    /// <summary>A relay that keeps nothing: a picture is "stored" under the link it came from, or under its size.</summary>
    private sealed class EchoRelay : IImageRelay
    {
        public bool IsEnabled => true;

        public Task<string> ResolveAsync(string imageUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(imageUrl);

        public Task<Result<string>> StoreAsync(byte[] content, string? contentType, CancellationToken cancellationToken = default)
        {
            var text = Encoding.UTF8.GetString(content);
            return Task.FromResult(Result<string>.Success(
                text.StartsWith("https://", StringComparison.Ordinal) && text.Length < 2_000
                    ? text
                    : $"embedded-picture://{content.Length}-bytes/{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content))[..12]}"));
        }

        public Task<RelayedImageContent?> ReadAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<RelayedImageContent?>(null);

        public Task<string?> ReadAsDataUrlAsync(string imageUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<ImageRelayReachability> CheckPublicReachabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImageRelayReachability(ImageRelayReachabilityStatus.NotConfigured, null, null));
    }
}

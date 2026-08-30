using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Indexes a single image linked directly from a library.
///
/// Nothing is fetched here: the embedding provider fetches the image itself when the chunk is
/// embedded, so downloading it now would only duplicate that work and prove nothing about whether the
/// provider can reach it.
/// </summary>
public sealed class DirectImageSourceExtractor : ISourceExtractor
{
    public string SourceType => SourceLinkClassifier.DirectImage;

    public bool CanHandle(Uri url) =>
        SourceLinkClassifier.Classify(url).SourceType == SourceLinkClassifier.DirectImage;

    public Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        // The catalogue entry is the only text a bare image has, and without it the image would carry
        // no text vector and be unreachable by a written question.
        var caption = string.Join(" — ", new[] { source.Country, source.Title, source.Author }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        var chunk = new ExtractedChunk(
            LocalKey: "0",
            SectionPath: source.Title ?? source.Country ?? source.NaturalKey,
            Text: caption.Length == 0 ? source.Url.ToString() : caption,
            ImageUrl: source.Url.ToString());

        return Task.FromResult(Result<ExtractedDocument>.Success(
            new ExtractedDocument(source.Title, SourceUpdatedAtUtc: null, [chunk])));
    }
}

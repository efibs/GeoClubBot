using UseCases.OutputPorts.AI.Ingestion;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Resolves the extractor for a URL or a stored source type.
///
/// Takes the registered extractors as an injected sequence, so adding a source family is one class
/// plus one DI line with no change here.
/// </summary>
public sealed class SourceExtractorRegistry(IEnumerable<ISourceExtractor> extractors) : ISourceExtractorRegistry
{
    private readonly List<ISourceExtractor> _extractors = [.. extractors];

    public ISourceExtractor? Resolve(Uri url) => _extractors.FirstOrDefault(extractor => extractor.CanHandle(url));

    public ISourceExtractor? ResolveByType(string sourceType) =>
        _extractors.FirstOrDefault(extractor =>
            extractor.SourceType.Equals(sourceType, StringComparison.OrdinalIgnoreCase));
}

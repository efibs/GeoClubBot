using Microsoft.Extensions.AI;
using UseCases.OutputPorts.AI;

namespace Infrastructure.OutputAdapters.AI;

/// <summary>
/// Bridges the two LLM-side services the rebuild loop needs: the categoriser that
/// produces the embedding text for each section, and the vLLM embedding model that
/// turns text into a vector. Used both during rebuild (per-section) and at search
/// time (per-query, no categoriser pass).
/// </summary>
public sealed class VllmPlonkItEmbedder(
    VllmEmbeddingService embeddingService,
    IPlonkItGuideEmbeddingTextProvider embeddingTextProvider) : IPlonkItEmbedder
{
    public async Task<bool> TestConnectionsAsync(CancellationToken cancellationToken = default)
    {
        if (!await embeddingTextProvider.TestConnectionAsync().ConfigureAwait(false))
        {
            return false;
        }
        return await embeddingService.TestConnectionAsync().ConfigureAwait(false);
    }

    public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var embedding = await embeddingService.GenerateAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false);
        return embedding.Vector;
    }

    public async Task<ReadOnlyMemory<float>> EmbedSectionAsync(PlonkItSection section, CancellationToken cancellationToken = default)
    {
        // IPlonkItGuideEmbeddingTextProvider takes ICollection<string>; the section carries
        // IReadOnlyList<string>, which doesn't satisfy that. Materialise once.
        var embeddingText = await embeddingTextProvider
            .GetEmbeddingTextAsync(section.GuideTitle, section.InnerText, section.Continents.ToList())
            .ConfigureAwait(false);

        var embedding = await embeddingService.GenerateAsync(embeddingText, cancellationToken: cancellationToken).ConfigureAwait(false);
        return embedding.Vector;
    }
}

using System.Net.Http.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace UseCases.UseCases.AI;

[Obsolete("Obsolete")]
public class VllmEmbeddingService(Uri endpoint, string modelName) : ITextEmbeddingGenerationService
{
    private readonly HttpClient _httpClient = new() { BaseAddress = endpoint };

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            model = modelName,
            input = data
        };

        var response = await _httpClient.PostAsJsonAsync("/v1/embeddings", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<VllmEmbeddingResponse>(cancellationToken).ConfigureAwait(false);
        
        if (result?.Data == null)
            throw new Exception("Failed to get embeddings from vLLM");

        return result.Data
            .OrderBy(e => e.Index)
            .Select(e => new ReadOnlyMemory<float>(e.Embedding))
            .ToList();
    }

    private class VllmEmbeddingResponse
    {
        public List<EmbeddingData> Data { get; set; } = [];
    }

    private class EmbeddingData
    {
        public int Index { get; set; }
        public float[] Embedding { get; set; } = [];
    }
}
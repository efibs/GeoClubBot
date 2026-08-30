using System.Net.Http.Json;
using Microsoft.Extensions.AI;

namespace Infrastructure.OutputAdapters.AI;

public class VllmEmbeddingService(Uri endpoint, string modelName) : IEmbeddingGenerator<string, Embedding<float>>
{
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);

        var request = new
        {
            model = modelName,
            input = values.ToList()
        };

        var response = await _httpClient
            .PostAsJsonAsync("v1/embeddings", request, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<VllmEmbeddingResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (result?.Data == null)
        {
            throw new Exception("Failed to get embeddings from vLLM");
        }

        // vLLM returns embeddings with an Index field; we must order so they match input order
        var ordered = result.Data.OrderBy(e => e.Index);

        var generated = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var item in ordered)
        {
            var emb = new Embedding<float>(new ReadOnlyMemory<float>(item.Embedding))
            {
                ModelId = modelName
            };

            generated.Add(emb);
        }

        return generated;
    }

    public async Task<bool> TestConnectionAsync()
    {
        var client = new HttpClient();

        try
        {
            var response = await client.GetAsync(endpoint + "health").ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    private sealed class VllmEmbeddingResponse
    {
        public List<EmbeddingData> Data { get; set; } = [];
    }

    private sealed class EmbeddingData
    {
        public int Index { get; set; }
        public float[] Embedding { get; set; } = [];
    }

    private readonly HttpClient _httpClient = new() { BaseAddress = endpoint };
}

using System.Net.Http.Json;
using Microsoft.Extensions.AI;

namespace UseCases.UseCases.AI;

public class VllmEmbeddingService(Uri endpoint, string modelName) : IEmbeddingGenerator<string, Embedding<float>>
{
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options,
        CancellationToken cancellationToken)
    {
        // Sanity check
        ArgumentNullException.ThrowIfNull(values);
        
        // Create the request
        var request = new
        {
            model = modelName,
            input = values.ToList()
        };
        
        // Call the embedding model
        var response = await _httpClient
            .PostAsJsonAsync("v1/embeddings", request, cancellationToken)
            .ConfigureAwait(false);
        
        // Ensure that the embedding succeeded
        response.EnsureSuccessStatusCode();
        
        // Read the result of the embedding call
        var result = await response.Content
            .ReadFromJsonAsync<VllmEmbeddingResponse>(cancellationToken)
            .ConfigureAwait(false);

        // Check that there is data
        if (result?.Data == null)
        {
            throw new Exception("Failed to get embeddings from vLLM");
        }
        
        // vLLM returns embeddings with an Index field; we must order so they match input order
        var ordered = result.Data.OrderBy(e => e.Index);
        
        // The return generated embeddings
        var generated = new GeneratedEmbeddings<Embedding<float>>();
        
        // For every embedding
        foreach (var item in ordered)
        {
            // Create the embedding object
            var emb = new Embedding<float>(new ReadOnlyMemory<float>(item.Embedding))
            {
                ModelId = modelName
            };
            
            // Add to embeddings
            generated.Add(emb);
        }
        
        return generated;
    }

    public async Task<bool> TestConnectionAsync()
    {
        // Create http client
        var client = new HttpClient();

        try
        {
            // Try get call to /health
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
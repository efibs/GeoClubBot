using System.Net.Http.Headers;
using System.Text.Json;
using Configuration;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Embeds with the bot's own embedder — same model, same request — and keeps every vector it has paid
/// for. A replay embeds the same questions on every run, and each request comes out of the daily
/// allowance the bot answers with; the cache makes a rerun free.
/// </summary>
/// <param name="requestsSent">Reads how many requests have left the process; absent when nothing is counted.</param>
/// <param name="owned">Disposed with the cache: the HTTP client behind the embedder.</param>
public sealed class EmbeddingCache(
    IEmbedder embedder,
    string path,
    Func<int>? requestsSent = null,
    IDisposable? owned = null) : IDisposable
{
    private readonly Dictionary<string, float[]> _vectors = Load(path);

    /// <summary>
    /// The bot's <see cref="OpenRouterEmbedder"/> behind <see cref="EmbeddingsOnlyHandler"/>, so nothing
    /// but an embeddings request can leave the probe for OpenRouter.
    /// </summary>
    public static EmbeddingCache ForOpenRouter(string apiKey, OpenRouterConfiguration openRouter, string path)
    {
        var guard = new EmbeddingsOnlyHandler(new HttpClientHandler());
        var httpClient = new HttpClient(guard)
        {
            BaseAddress = new Uri(openRouter.BaseUrl),
            Timeout = TimeSpan.FromMinutes(3)
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpClient.DefaultRequestHeaders.Add("X-Title", $"{openRouter.AppName} retrieval probe");

        var embedder = new OpenRouterEmbedder(
            new SingleClientFactory(httpClient),
            Options.Create(new AiConfiguration { OpenRouter = openRouter }),
            NullLogger<OpenRouterEmbedder>.Instance);

        return new EmbeddingCache(embedder, path, () => guard.RequestsSent, httpClient);
    }

    /// <summary>Requests this run has sent. Zero when everything came from the cache.</summary>
    public int RequestsSent => requestsSent?.Invoke() ?? 0;

    public string CachePath => path;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var missing = texts
            .Distinct(StringComparer.Ordinal)
            .Where(text => !_vectors.ContainsKey(Key(text)))
            .ToList();

        if (missing.Count > 0)
        {
            var embedded = await embedder
                .EmbedAsync([.. missing.Select(text => (EmbeddingInput)new TextEmbeddingInput(text))], cancellationToken)
                .ConfigureAwait(false);

            if (embedded.IsFailure)
            {
                throw new ProbeException($"The questions could not be embedded: {embedded.Error.Message}");
            }

            for (var index = 0; index < missing.Count; index++)
            {
                _vectors[Key(missing[index])] = embedded.Value[index].ToArray();
            }

            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(_vectors), cancellationToken).ConfigureAwait(false);
        }

        return [.. texts.Select(text => _vectors[Key(text)])];
    }

    public void Dispose() => owned?.Dispose();

    /// <summary>Keyed by model too, so a change of embedding model never reuses an incomparable vector.</summary>
    private string Key(string text) => $"{embedder.ModelId}\u001F{text}";

    private static Dictionary<string, float[]> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, float[]>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, float[]>>(File.ReadAllText(path))
                   ?? new Dictionary<string, float[]>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A cache is only ever a saving; a damaged one is started afresh rather than trusted.
            return new Dictionary<string, float[]>(StringComparer.Ordinal);
        }
    }

    /// <summary>The embedder asks a factory for its client; this one only ever has the guarded one.</summary>
    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

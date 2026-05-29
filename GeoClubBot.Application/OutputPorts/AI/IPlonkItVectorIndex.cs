namespace UseCases.OutputPorts.AI;

/// <summary>
/// Vector-store-only port. Owns collection lifecycle, point upsert, and the search/scroll
/// queries the chat plugin runs. Implementations are vendor-specific (Qdrant today).
/// </summary>
public interface IPlonkItVectorIndex
{
    Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default);

    Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default);

    Task DeleteCollectionAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(
        string id,
        ReadOnlyMemory<float> vector,
        string text,
        string source,
        string country,
        CancellationToken cancellationToken = default);

    Task<List<SectionRecord>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int limit,
        CancellationToken cancellationToken = default);

    Task<List<string>> GetUniqueCountriesAsync(CancellationToken cancellationToken = default);

    Task<List<SectionRecord>> GetSectionsByCountryAsync(string country, CancellationToken cancellationToken = default);
}

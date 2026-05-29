namespace UseCases.OutputPorts.AI;

/// <summary>
/// Scrapes the PlonkIt guide site, emitting a mixed stream of parsed sections and
/// human-readable status updates. Implementations are free to fan out internally.
/// </summary>
public interface IPlonkItPageFetcher
{
    IAsyncEnumerable<PlonkItFetchEvent> EnumerateAsync(CancellationToken cancellationToken = default);
}

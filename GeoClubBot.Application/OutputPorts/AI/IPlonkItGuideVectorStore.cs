namespace UseCases.OutputPorts.AI;

public interface IPlonkItGuideVectorStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> RebuildStoreAsync(CancellationToken cancellationToken = default);
}

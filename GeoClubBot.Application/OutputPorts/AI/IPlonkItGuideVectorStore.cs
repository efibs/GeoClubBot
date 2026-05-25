namespace UseCases.OutputPorts.AI;

public interface IPlonkItGuideVectorStore
{
    Task InitializeAsync();

    IAsyncEnumerable<string> RebuildStoreAsync();
}

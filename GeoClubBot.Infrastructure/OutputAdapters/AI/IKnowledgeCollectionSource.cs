namespace Infrastructure.OutputAdapters.AI;

/// <summary>
/// Which Qdrant collection the knowledge index reads and writes, and the width of its vectors. Both
/// follow the embedding model in use, which can change while the bot runs when OpenRouter stops serving
/// one (#503), so the index asks for them on every call instead of being handed them once.
/// </summary>
public interface IKnowledgeCollectionSource
{
    /// <summary>The collection in use now; before <see cref="ResolveAsync"/> has run, the configured one.</summary>
    string CollectionName { get; }

    int VectorSize { get; }

    /// <summary>Settles which model, and so which collection, is in use. Cheap once it has.</summary>
    Task ResolveAsync(CancellationToken cancellationToken = default);
}

/// <summary>A collection that never changes, for tests and the read-only probe.</summary>
public sealed class FixedKnowledgeCollection(string collectionName, int vectorSize) : IKnowledgeCollectionSource
{
    public string CollectionName => collectionName;

    public int VectorSize => vectorSize;

    public Task ResolveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

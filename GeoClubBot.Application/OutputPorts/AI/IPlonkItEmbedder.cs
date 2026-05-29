namespace UseCases.OutputPorts.AI;

/// <summary>
/// Wraps the LLM-categoriser and the embedding model behind one port. Owns
/// connection-test responsibility for both upstream services.
/// </summary>
public interface IPlonkItEmbedder
{
    Task<bool> TestConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Embed a raw search query verbatim — used by the chat-plugin search path.</summary>
    Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate the categoriser-derived embedding text for a section and then embed it.
    /// Used by the rebuild path.
    /// </summary>
    Task<ReadOnlyMemory<float>> EmbedSectionAsync(PlonkItSection section, CancellationToken cancellationToken = default);
}

using Utilities;

namespace UseCases.OutputPorts.AI;

/// <summary>Sends chat completions upstream. The only outbound AI dependency at runtime.</summary>
public interface IChatModelClient
{
    Task<Result<AiChatResponse>> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's currently advertised free models. Returns only zero-cost entries — the whole
    /// feature is built to run without spend.
    /// </summary>
    Task<Result<IReadOnlyList<ChatModelDescriptor>>> ReadFreeModelsAsync(CancellationToken cancellationToken = default);
}

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

    /// <summary>
    /// How well the model's providers have been serving it. Costs no chat request: the roster lists a
    /// model whether or not anything behind it is answering, and this is where that shows.
    /// </summary>
    Task<Result<ChatModelHealth>> ReadModelHealthAsync(string modelId, CancellationToken cancellationToken = default);
}

/// <param name="UptimeLastDayPercent">
/// Share of the last day's requests the model's best provider endpoint served, or <c>null</c> when the
/// provider published no figure.
/// </param>
public sealed record ChatModelHealth(double? UptimeLastDayPercent);

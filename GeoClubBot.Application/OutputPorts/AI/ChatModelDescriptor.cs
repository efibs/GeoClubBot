namespace UseCases.OutputPorts.AI;

/// <summary>
/// A chat model that is currently offered by the upstream provider, reduced to the facts the
/// selector needs. Deliberately vendor-neutral: the OpenRouter wire format never leaves the
/// infrastructure layer.
/// </summary>
/// <param name="Id">Provider model id, e.g. <c>google/gemma-4-31b-it:free</c>.</param>
/// <param name="ContextLength">Total context window in tokens.</param>
/// <param name="MaxCompletionTokens">Completion cap, or <c>null</c> when the provider does not say.</param>
/// <param name="ProducesTextOnly">
/// Whether text is the only thing the model emits, which is what separates a chat model from the
/// music and image generators sharing the roster. Those declare text <em>and</em> audio or image
/// output, and are billed per second or per picture rather than per token — so both token prices read
/// "0" and a free-price filter waves them straight through.
/// </param>
/// <param name="ExpiresAt">
/// When the provider has announced a retirement date. Free models frequently carry one, which is the
/// main reason a hardcoded model id rots.
/// </param>
public sealed record ChatModelDescriptor(
    string Id,
    string Name,
    int ContextLength,
    int? MaxCompletionTokens,
    bool SupportsImageInput,
    bool ProducesTextOnly,
    bool SupportsTools,
    bool SupportsStructuredOutputs,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

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
/// <param name="IsGuardrail">
/// Whether the model is a safety classifier rather than an assistant. These moderate other models'
/// traffic and answer with a verdict — "User Safety: safe" — so one picked to answer a question
/// replies with that instead of an answer, and nothing about the exchange looks like a failure.
/// They are indistinguishable from chat models structurally: same text-to-text modality, same free
/// price, large context windows, so they rank well. Only the provider's own prose gives them away.
/// </param>
/// <param name="ExpiresAt">
/// When the provider has announced a retirement date. Free models frequently carry one, which is the
/// main reason a hardcoded model id rots.
/// </param>
/// <param name="TotalParametersBillions">
/// Parameter count in billions, or <c>null</c> when neither the id, the name nor the provider's prose
/// states it. Size is the best quality signal the roster offers. Asked the same grounded question in
/// October 2026, the 550B Nemotron Ultra answered concisely and said what the guides did not cover,
/// while the 30B Nemotron Lightning, 3B of it active, spent its whole 4,096-token allowance reasoning
/// and returned nothing at all.
/// </param>
/// <param name="ActiveParametersBillions">
/// Parameters active per token, for a mixture-of-experts model. <c>null</c> for a dense model, or when
/// the provider does not say.
/// </param>
/// <param name="IsCodingSpecialist">
/// Whether the provider describes the model as built for coding agents. Those are tuned to edit code
/// and drive terminals, not to explain things in prose — which is all this bot ever asks of a model.
/// </param>
/// <param name="UptimeLastDayPercent">
/// Share of requests the model's best provider endpoint served over the last day, or <c>null</c> when
/// it has not been measured. Read separately from the roster, which says nothing about health: on
/// 2026-10-23 Nemotron 3 Super was listed like any other model while its only provider had served 0%
/// of the day's requests.
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
    bool IsGuardrail,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    double? TotalParametersBillions = null,
    double? ActiveParametersBillions = null,
    bool IsCodingSpecialist = false,
    double? UptimeLastDayPercent = null)
{
    /// <summary>
    /// One number to compare a dense model with a mixture of experts: the geometric mean of total and
    /// active parameters, a common rule of thumb for what a sparse model performs like. A 550B model
    /// running 55B per token compares with a dense 174B one, not with either figure alone. A dense
    /// model is its total. A model that states only its active count has no effective size: that count is
    /// only a floor, and a 30B mixture of experts running 3B per token, credited with the 3B, would fall
    /// below the selector's size bar although it compares with a dense 9.5B model. Unknown is held to no
    /// bar and ranks in the middle.
    /// </summary>
    public double? EffectiveParametersBillions => (TotalParametersBillions, ActiveParametersBillions) switch
    {
        ({ } total, { } active) when total > 0 && active > 0 => Math.Sqrt(total * active),
        ({ } total, null) when total > 0 => total,
        _ => null
    };
}

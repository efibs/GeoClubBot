using UseCases.OutputPorts.AI;
using Utilities;

namespace UseCases.UseCases.AI.Conversations;

/// <summary>
/// Decides what a failed answer attempt means: whether the models asked deserve the blame, and whether
/// asking others is worth another request from the daily allowance.
///
/// Pure, so the rules are pinned by tests rather than buried in the handler's control flow.
/// </summary>
public static class AnswerAttemptPolicy
{
    /// <summary>
    /// A retry asks different models, so it only helps when the models were the problem. It is refused
    /// when the provider could not be reached, is throttling us, or refused the request as sent — other
    /// models sit behind the same provider, the same key and the same request — and after a slow
    /// attempt, since a provider that is struggling would only double the wait for an answer that is
    /// likely to fail again. A 403 or 429 that one model earned on its own — a free model reserved for
    /// agentic harnesses, a provider's shared pool running dry — is not refused: on 2026-10-23 other
    /// models answered the very request those two failed.
    /// </summary>
    public static bool ShouldRetry(Error error, TimeSpan elapsed, TimeSpan patience) =>
        error.Code is not (ChatErrorCodes.RateLimited
            or ChatErrorCodes.Unreachable
            or ChatErrorCodes.Rejected
            or ChatErrorCodes.NoModelAvailable)
        && elapsed <= patience;

    /// <summary>
    /// Which models of a failed chain the failure says something about. A server error or an empty
    /// completion speaks to the whole chain. A refusal to serve this bot, or a provider's shared pool
    /// running dry, speaks only to the model it names — the first — and blaming the rest would demote
    /// healthy models for one model's trouble. An outage, our own rate limit or a request refused as
    /// sent speaks to none of them, and blaming the chain for one would demote the best models on the
    /// roster all at once.
    /// </summary>
    public static AnswerFailureBlame AssignBlame(Error error) => error.Code switch
    {
        ChatErrorCodes.RequestFailed or ChatErrorCodes.EmptyResponse => AnswerFailureBlame.WholeChain,
        ChatErrorCodes.ModelRateLimited => AnswerFailureBlame.FirstModel,
        ChatErrorCodes.ModelGated => AnswerFailureBlame.FirstModelUnfit,
        _ => AnswerFailureBlame.None
    };
}

/// <summary>What a failed chain is blamed for, and on whom.</summary>
public enum AnswerFailureBlame
{
    /// <summary>The failure is not the models' doing.</summary>
    None = 0,

    /// <summary>Every model of the chain failed; each is demoted for a while.</summary>
    WholeChain,

    /// <summary>Only the first model failed, for a reason that passes; it alone is demoted for a while.</summary>
    FirstModel,

    /// <summary>The first model will never answer this bot; it is excluded until restart.</summary>
    FirstModelUnfit
}

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
    /// likely to fail again.
    /// </summary>
    public static bool ShouldRetry(Error error, TimeSpan elapsed, TimeSpan patience) =>
        error.Code is not (ChatErrorCodes.RateLimited
            or ChatErrorCodes.Unreachable
            or ChatErrorCodes.Rejected
            or ChatErrorCodes.NoModelAvailable)
        && elapsed <= patience;

    /// <summary>
    /// Whether a failed chain says anything about its models. A server error or an empty completion
    /// does; an outage, our own rate limit or a request refused as sent does not, and blaming the chain
    /// for one would demote the best models on the roster all at once.
    /// </summary>
    public static bool BlamesModels(Error error) =>
        error.Code is ChatErrorCodes.RequestFailed or ChatErrorCodes.EmptyResponse;
}

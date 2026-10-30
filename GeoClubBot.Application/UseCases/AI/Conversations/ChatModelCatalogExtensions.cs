using UseCases.OutputPorts.AI;
using Utilities;

namespace UseCases.UseCases.AI.Conversations;

public static class ChatModelCatalogExtensions
{
    /// <summary>
    /// Demotes whichever models a failed request was their fault — all of them, only the head, or the
    /// head for good — and none when the provider or the request itself was to blame. Shared by the
    /// answer, its tool rounds and the query planner, so a call that follows a failed one skips the
    /// same models.
    /// </summary>
    public static void ReportFailedRequest(this IChatModelCatalog catalog, IReadOnlyList<string> chain, Error failure)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(chain);

        switch (AnswerAttemptPolicy.AssignBlame(failure))
        {
            case AnswerFailureBlame.WholeChain:
                catalog.ReportChainOutcome(chain, answeredBy: null);
                break;
            case AnswerFailureBlame.FirstModel when chain.Count > 0:
                catalog.ReportFailure(chain[0]);
                break;
            case AnswerFailureBlame.FirstModelUnfit when chain.Count > 0:
                catalog.ReportUnfit(chain[0]);
                break;
        }
    }
}

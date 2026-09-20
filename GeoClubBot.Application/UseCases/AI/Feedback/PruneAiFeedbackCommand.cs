using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Feedback;

/// <summary>
/// Deletes archived feedback past its own retention window.
///
/// Kept separate from <c>PruneAiConversationsCommand</c> so the two windows stay independent: the
/// archive outliving the conversation sweep is the entire point of it, and the default here is to
/// keep everything forever.
/// </summary>
public sealed record PruneAiFeedbackCommand : ICommand<Result<int>>;

public sealed class PruneAiFeedbackHandler(
    IAiFeedbackRepository feedbacks,
    IOptions<AiFeedbackConfiguration> configuration)
    : IRequestHandler<PruneAiFeedbackCommand, Result<int>>
{
    public async Task<Result<int>> Handle(PruneAiFeedbackCommand request, CancellationToken cancellationToken)
    {
        var retentionDays = configuration.Value.RetentionDays;
        if (retentionDays <= 0)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        return await feedbacks.DeleteOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false);
    }
}

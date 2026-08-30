using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Conversations;

/// <summary>
/// Deletes conversation history past its retention window.
///
/// Storing what people asked the bot is a privacy posture, not just a storage cost, so the window is
/// configurable and enforced on a schedule rather than left to grow indefinitely.
/// </summary>
public sealed record PruneAiConversationsCommand : ICommand<Result<int>>;

public sealed class PruneAiConversationsHandler(
    IAiConversationRepository conversations,
    IOptions<AiConversationConfiguration> configuration)
    : IRequestHandler<PruneAiConversationsCommand, Result<int>>
{
    public async Task<Result<int>> Handle(PruneAiConversationsCommand request, CancellationToken cancellationToken)
    {
        var retentionDays = Math.Max(1, configuration.Value.RetentionDays);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        return await conversations.DeleteOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false);
    }
}

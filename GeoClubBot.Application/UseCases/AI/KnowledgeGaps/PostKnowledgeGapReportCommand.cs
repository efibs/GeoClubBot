using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using Utilities;

namespace UseCases.UseCases.AI.KnowledgeGaps;

/// <summary>
/// The scheduled run: forgets weak retrievals past retention, and — when <c>PostReport</c> is on —
/// posts the report to the configured channel.
/// </summary>
public sealed record PostKnowledgeGapReportCommand : ICommand<Result<bool>>;

public sealed partial class PostKnowledgeGapReportHandler(
    ISender mediator,
    IAiRetrievalGapRepository gaps,
    IAiChannelPublisher publisher,
    IOptions<AiKnowledgeGapsConfiguration> configuration,
    TimeProvider timeProvider,
    ILogger<PostKnowledgeGapReportHandler> logger)
    : IRequestHandler<PostKnowledgeGapReportCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(PostKnowledgeGapReportCommand request, CancellationToken cancellationToken)
    {
        var settings = configuration.Value;

        var removed = await gaps.DeleteOlderThanAsync(timeProvider.GetUtcNow() - TimeSpan.FromDays(settings.RetentionDays), cancellationToken)
            .ConfigureAwait(false);
        LogPruned(logger, removed, settings.RetentionDays);

        if (!settings.PostReport || settings.TextChannelId is not { } channel)
        {
            return false;
        }

        var report = await mediator.Send(new ReadKnowledgeGapReportQuery(), cancellationToken).ConfigureAwait(false);
        if (report.IsFailure)
        {
            return report.Error;
        }

        var answer = new AiAnswer(KnowledgeGapReportText.Render(report.Value), [], [], string.Empty, 0, 0, false, [], []);
        var posted = await publisher.PostAsync(channel, answer, cancellationToken).ConfigureAwait(false);
        return posted is not null;
    }

    [LoggerMessage(LogLevel.Information, "Forgot {Removed} weak retrieval(s) older than {Days} days.")]
    static partial void LogPruned(ILogger logger, int removed, int days);
}

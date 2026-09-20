using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Feedback;

/// <summary>
/// Counts of how the answers have been rated, for the admin read-out.
/// </summary>
/// <param name="Days">Window to count over; null counts the whole archive.</param>
public sealed record ReadAiFeedbackSummaryQuery(int? Days) : IQuery<Result<AiFeedbackCounts>>;

public sealed class ReadAiFeedbackSummaryHandler(IAiFeedbackRepository feedbacks)
    : IRequestHandler<ReadAiFeedbackSummaryQuery, Result<AiFeedbackCounts>>
{
    public async Task<Result<AiFeedbackCounts>> Handle(
        ReadAiFeedbackSummaryQuery request,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? since = request.Days is > 0
            ? DateTimeOffset.UtcNow.AddDays(-request.Days.Value)
            : null;

        return Result<AiFeedbackCounts>.Success(
            await feedbacks.ReadSummaryAsync(since, cancellationToken).ConfigureAwait(false));
    }
}

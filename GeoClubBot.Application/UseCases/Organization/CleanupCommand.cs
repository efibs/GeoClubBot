using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.Organization;

public sealed record CleanupCommand : ICommand;

public sealed partial class CleanupHandler(
    IExcusesRepository excuses,
    IHistoryRepository history,
    IClubMemberRepository clubMembers,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig,
    ILogger<CleanupHandler> logger) : IRequestHandler<CleanupCommand, Unit>
{
    public async Task<Unit> Handle(CleanupCommand request, CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow.Subtract(activityCheckerConfig.Value.HistoryKeepTimeSpan);

        var deletedExcuses = await excuses.DeleteExcusesBeforeAsync(threshold, cancellationToken).ConfigureAwait(false);
        var deletedHistoryEntries = await history.DeleteHistoryEntriesBeforeAsync(threshold, cancellationToken).ConfigureAwait(false);
        var deletedMembers = await clubMembers.DeleteClubMembersWithoutHistoryAndStrikesAsync(cancellationToken).ConfigureAwait(false);

        LogDeleteResults(logger, deletedExcuses, deletedHistoryEntries, deletedMembers);

        return Unit.Value;
    }

    [LoggerMessage(LogLevel.Information,
        "Deleted {deletedExcuses} excuses, {deletedHistoryEntries} history entries and {deletedMembers} members.")]
    static partial void LogDeleteResults(ILogger<CleanupHandler> logger, int deletedExcuses, int deletedHistoryEntries, int deletedMembers);
}

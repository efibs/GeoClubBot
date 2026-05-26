using Constants;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Organization;

public sealed record CleanupCommand : ICommand;

public sealed partial class CleanupHandler(
    IExcusesRepository excuses,
    IHistoryRepository history,
    IClubMemberRepository clubMembers,
    IConfiguration config,
    ILogger<CleanupHandler> logger) : IRequestHandler<CleanupCommand, Unit>
{
    private readonly TimeSpan _historyKeepThreshold =
        config.GetValue<TimeSpan>(ConfigKeys.ActivityCheckerHistoryKeepTimeSpanConfigurationKey);

    public async Task<Unit> Handle(CleanupCommand request, CancellationToken cancellationToken)
    {
        var threshold = DateTime.UtcNow.Subtract(_historyKeepThreshold);

        var deletedExcuses = await excuses.DeleteExcusesBeforeAsync(threshold).ConfigureAwait(false);
        var deletedHistoryEntries = await history.DeleteHistoryEntriesBeforeAsync(threshold).ConfigureAwait(false);
        var deletedMembers = await clubMembers.DeleteClubMembersWithoutHistoryAndStrikesAsync().ConfigureAwait(false);

        LogDeleteResults(logger, deletedExcuses, deletedHistoryEntries, deletedMembers);

        return Unit.Value;
    }

    [LoggerMessage(LogLevel.Information,
        "Deleted {deletedExcuses} excuses, {deletedHistoryEntries} history entries and {deletedMembers} members.")]
    static partial void LogDeleteResults(ILogger<CleanupHandler> logger, int deletedExcuses, int deletedHistoryEntries, int deletedMembers);
}

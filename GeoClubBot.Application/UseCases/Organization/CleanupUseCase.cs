using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.Organization;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Organization;

public partial class CleanupUseCase(
    IUnitOfWork unitOfWork,
    IConfiguration config,
    ILogger<CleanupUseCase> logger) : ICleanupUseCase
{
    public async Task DoCleanupAsync()
    {
        // Calculate the threshold
        var threshold = DateTime.UtcNow.Subtract(_historyKeepThreshold);
        
        // Cleanup excuses
        var deletedExcuses = await unitOfWork.Excuses.DeleteExcusesBeforeAsync(threshold).ConfigureAwait(false);

        // Cleanup History
        var deletedHistoryEntries = await unitOfWork.History.DeleteHistoryEntriesBeforeAsync(threshold).ConfigureAwait(false);
        
        // Cleanup members that have no history anymore
        var deletedMembers = await unitOfWork.ClubMembers.DeleteClubMembersWithoutHistoryAndStrikesAsync().ConfigureAwait(false);
        
        // Print info log
        LogDeleteResults(logger, deletedExcuses, deletedHistoryEntries, deletedMembers);
    }

    private readonly TimeSpan _historyKeepThreshold =
        config.GetValue<TimeSpan>(ConfigKeys.ActivityCheckerHistoryKeepTimeSpanConfigurationKey);

    [LoggerMessage(LogLevel.Information, "Deleted {deletedExcuses} excuses, {deletedHistoryEntries} history entries and {deletedMembers} members.")]
    static partial void LogDeleteResults(ILogger<CleanupUseCase> logger, int deletedExcuses, int deletedHistoryEntries, int deletedMembers);
}
using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.Organization;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Organization;

public class CleanupUseCase(
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
        logger.LogInformation($"Deleted {deletedExcuses} excuses, {deletedHistoryEntries} history entries and {deletedMembers} members.");
    }

    private readonly TimeSpan _historyKeepThreshold =
        config.GetValue<TimeSpan>(ConfigKeys.ActivityCheckerHistoryKeepTimeSpanConfigurationKey);
}
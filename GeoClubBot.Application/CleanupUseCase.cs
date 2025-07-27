using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class CleanupUseCase(
    IHistoryRepository historyRepository,
    IExcusesRepository excusesRepository,
    IClubMemberRepository clubMemberRepository,
    IConfiguration config,
    ILogger<CleanupUseCase> logger) : ICleanupUseCase
{
    public async Task DoCleanupAsync()
    {
        // Calculate the threshold
        var threshold = DateTime.UtcNow.Subtract(_historyKeepThreshold);
        
        // Cleanup excuses
        var deletedExcuses = await excusesRepository.DeleteExcusesBeforeAsync(threshold);

        // Cleanup History
        var deletedHistoryEntries = await historyRepository.DeleteHistoryEntriesBeforeAsync(threshold);
        
        // Cleanup members that have no history anymore
        var deletedMembers = await clubMemberRepository.DeleteClubMembersWithoutHistoryAndStrikesAsync();
        
        // Print info log
        logger.LogInformation($"Deleted {deletedExcuses} excuses, {deletedHistoryEntries} history entries and {deletedMembers} members.");
    }

    private readonly TimeSpan _historyKeepThreshold =
        config.GetValue<TimeSpan>(ConfigKeys.ActivityCheckerHistoryKeepTimeSpanConfigurationKey);
}
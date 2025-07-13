using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class CleanupUseCase(
    IActivityRepository activityRepository,
    IExcusesRepository excusesRepository,
    IConfiguration config,
    ILogger<CleanupUseCase> logger) : ICleanupUseCase
{
    public async Task DoCleanupAsync()
    {
        // Calculate the threshold
        var threshold = DateTime.UtcNow.Subtract(_historyKeepThreshold);
        
        // Cleanup each category
        await _cleanupActivityHistoryAsync(threshold);
        await _cleanupLatestActivityAsync(threshold);
        await _cleanupStatusesAsync(threshold);
        await _cleanupExcusesAsync(threshold);
    }

    private async Task _cleanupActivityHistoryAsync(DateTimeOffset threshold)
    {
        // Store statistics
        var numChangedMembers = 0;
        var numDeletedEntries = 0;
        
        // Read the entire history
        var currentHistory = await activityRepository.ReadActivityHistoryAsync();
        
        // Build the new history
        var newHistory = new Dictionary<string, List<GeoGuessrClubMemberActivityEntry>>();
        
        // For every member in the current history
        foreach (var member in currentHistory)
        {
            // Get the members filtered history
            var filteredHistory = member.Value
                .Where(e => e.Timestamp >= threshold)
                .ToList();
            
            // Set in new history
            newHistory.Add(member.Key, filteredHistory);
            
            // If the history of the member changed
            if (filteredHistory.Count != member.Value.Count)
            {
                // Update statistics
                numChangedMembers++;
                numDeletedEntries += member.Value.Count - filteredHistory.Count;
            }
        }
        
        // Write the new history
        await activityRepository.OverwriteActivityHistoryAsync(newHistory);
        
        // Log debug
        logger.LogDebug($"Deleted {numDeletedEntries} history entries of {numChangedMembers} members before {threshold:g}");
    }

    private async Task _cleanupLatestActivityAsync(DateTimeOffset threshold)
    {
        // Read the latest activities
        var currentLatestActivities = await activityRepository.ReadLatestActivityEntriesAsync();
        
        // Get the new latest activities
        var newLatestActivities = currentLatestActivities
            .Where(e => e.Value.Timestamp >= threshold)
            .ToDictionary();
        
        // Save the new latest activities
        await activityRepository.OverwriteLatestActivityEntriesAsync(newLatestActivities);
        
        // Log debug
        logger.LogDebug($"Deleted {currentLatestActivities.Count - newLatestActivities.Count} latest activities before {threshold:g}");
    }

    private async Task _cleanupStatusesAsync(DateTimeOffset threshold)
    {
        // Read the statuses
        var currentStatuses = await activityRepository.ReadActivityStatusesAsync();
        
        // Get the new statuses
        var newStatuses = currentStatuses
            .Where(s => s.Value.Timestamp >= threshold)
            .ToDictionary();
        
        // Overwrite the statuses
        await activityRepository.OverwriteActivityStatusesAsync(newStatuses);
        
        // Log debug
        logger.LogDebug($"Deleted {currentStatuses.Count - newStatuses.Count} statuses before {threshold:g}");
    }

    private async Task _cleanupExcusesAsync(DateTimeOffset threshold)
    {
        // Read all excuses
        var allExcuses = await excusesRepository.ReadAllExcusesAsync();
        
        // Filter out the excuses that are too far gone
        var belowThresholdExcuses = allExcuses
            .Where(e => e.To < threshold)
            .ToList();
        
        // Extract guids
        var toBeDeletedExcuseIds = belowThresholdExcuses
            .Select(e => e.Id)
            .ToList();
        
        // Delete the excuses
        var numDeletedExcuses = await excusesRepository.DeleteExcusesAsync(toBeDeletedExcuseIds);
        
        // Log debug
        logger.LogDebug($"Deleted {numDeletedExcuses} excuses before {threshold:g}");
    }
    
    private readonly TimeSpan _historyKeepThreshold =
        config.GetValue<TimeSpan>(ConfigKeys.ActivityCheckerHistoryKeepTimeSpanConfigurationKey);
}
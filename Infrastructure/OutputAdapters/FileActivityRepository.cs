using System.Text.Json;
using Entities;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

/// <summary>
/// Repository to persist the activity on the filesystem.
/// </summary>
public class FileActivityRepository : IActivityRepository
{
    private static readonly string DataFolderPath = Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "data");
    private static readonly string ActivityHistoryFileName = Path.Combine(DataFolderPath, "ActivityHistory.json");
    private static readonly string LatestActivityFileName = Path.Combine(DataFolderPath, "LatestActivity.json");
    private static readonly string StatusesFileName = Path.Combine(DataFolderPath, "Statuses.json");
    
    private static readonly SemaphoreSlim Lock = new SemaphoreSlim(1, 1);

    public FileActivityRepository()
    {
        // Create the directories if they do not exist
        Directory.CreateDirectory(DataFolderPath);

        // Check if the activity history file exists
        if (!File.Exists(ActivityHistoryFileName))
        {
            // Create the file with an empty dictionary
            File.WriteAllText(ActivityHistoryFileName, "{}");
        }

        // Check if the latest activity file exists
        if (!File.Exists(LatestActivityFileName))
        {
            // Create the file with an empty dictionary
            File.WriteAllText(LatestActivityFileName, "{}");
        }

        // Check if the statuses file exists
        if (!File.Exists(StatusesFileName))
        {
            // Create the file with an empty dictionary
            File.WriteAllText(StatusesFileName, "{}");
        }
    }

    public async Task WriteActivityEntriesAsync(Dictionary<string, GeoGuessrClubMemberActivityEntry> entries)
    {
        // Acquire the lock
        await Lock.WaitAsync();
        
        try
        {
            // Read the activity history file
            var currentActivityHistoryJson = await File.ReadAllTextAsync(ActivityHistoryFileName);

            // Parse from json
            var currentActivityHistory =
                JsonSerializer.Deserialize<Dictionary<string, List<GeoGuessrClubMemberActivityEntry>>>(
                    currentActivityHistoryJson);

            // Sanity check
            if (currentActivityHistory == null)
            {
                throw new InvalidOperationException("Activity history is malformed");
            }

            // Read the latest activity file
            var currentLatestActivityJson = await File.ReadAllTextAsync(LatestActivityFileName);

            // Parse from json
            var currentLatestActivity =
                JsonSerializer.Deserialize<Dictionary<string, GeoGuessrClubMemberActivityEntry>>(
                    currentLatestActivityJson);

            // Sanity check
            if (currentLatestActivity == null)
            {
                throw new InvalidOperationException("Latest activity is malformed");
            }

            // Get a list of all user ids past and current members
            var userIds = currentActivityHistory.Keys.ToList().Union(entries.Keys.ToList());

            // Create the new activity history
            var newActivityHistory = userIds.ToDictionary(uId => uId,
                uId =>
                {
                    // Get the current history
                    currentActivityHistory.TryGetValue(uId, out var currentHistoryList);

                    // Set to empty list if not found
                    currentHistoryList ??= [];

                    // Append the new entry to the list if the user has a new entry.
                    // Otherwise, just leave the entries unmodified.
                    return !entries.TryGetValue(uId, out var entry)
                        ? currentHistoryList
                        : currentHistoryList.Append(entry);
                });

            // Convert the new activity history to json
            var newActivityHistoryJson = JsonSerializer.Serialize(newActivityHistory);

            // Write the new activity history
            await File.WriteAllTextAsync(ActivityHistoryFileName, newActivityHistoryJson);

            // Update the latest activity
            var newLatestActivity = new Dictionary<string, GeoGuessrClubMemberActivityEntry>(currentLatestActivity);
            foreach (var entry in entries)
            {
                newLatestActivity[entry.Key] = entry.Value;
            }

            // Convert the new latest activity to json
            var newLatestActivityJson = JsonSerializer.Serialize(newLatestActivity);

            // Write the new latest activity
            await File.WriteAllTextAsync(LatestActivityFileName, newLatestActivityJson);
        }
        finally
        {
            // Release the lock
            Lock.Release();
        }
    }

    public async Task<Dictionary<string, GeoGuessrClubMemberActivityEntry>> ReadLatestActivityEntriesAsync()
    {
        // Acquire the lock
        await Lock.WaitAsync();

        try
        {
            // Read the latest activity file
            var latestActivityJson = await File.ReadAllTextAsync(LatestActivityFileName);

            // Parse from json
            var latestActivities =
                JsonSerializer.Deserialize<Dictionary<string, GeoGuessrClubMemberActivityEntry>>(latestActivityJson);

            return latestActivities ?? new Dictionary<string, GeoGuessrClubMemberActivityEntry>();
        }
        finally
        {
            // Release the lock
            Lock.Release();
        }
    }

    public async Task WriteMemberStatusesAsync(Dictionary<string, GeoGuessrClubMemberActivityStatus> statuses)
    {
        // Acquire the lock
        await Lock.WaitAsync();

        try
        {
            // Read the statuses file
            var currentStatusesJson = await File.ReadAllTextAsync(StatusesFileName);

            // Parse from json
            var currentStatuses =
                JsonSerializer.Deserialize<Dictionary<string, GeoGuessrClubMemberActivityStatus>>(
                    currentStatusesJson);

            // Sanity check
            if (currentStatuses == null)
            {
                throw new InvalidOperationException("Statuses are malformed");
            }

            // Update the statuses
            var newStatuses = new Dictionary<string, GeoGuessrClubMemberActivityStatus>(currentStatuses);
            foreach (var entry in statuses)
            {
                newStatuses[entry.Key] = entry.Value;
            }

            // Convert the new statuses to json
            var newStatusesJson = JsonSerializer.Serialize(newStatuses);

            // Write the new latest activity
            await File.WriteAllTextAsync(StatusesFileName, newStatusesJson);
        }
        finally
        {
            // Release the lock
            Lock.Release();
        }
    }

    public async Task<Dictionary<string, GeoGuessrClubMemberActivityStatus>> ReadActivityStatusesAsync()
    {
        // Acquire the lock
        await Lock.WaitAsync();

        try
        {
            // Read the latest activity file
            var statusesJson = await File.ReadAllTextAsync(StatusesFileName);

            // Parse from json
            var statuses =
                JsonSerializer.Deserialize<Dictionary<string, GeoGuessrClubMemberActivityStatus>>(statusesJson);

            return statuses ?? new Dictionary<string, GeoGuessrClubMemberActivityStatus>();
        }
        finally
        {
            // Release the lock
            Lock.Release();
        }
    }
}